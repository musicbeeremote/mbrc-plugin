//! On-disk album cover cache (the Rust port of C# `CoverCache` + the cache half
//! of `CoverService`). The core owns identities, resizing, storage, and serving;
//! the C# host only provides raw ingredients (album list, track paths, mod
//! times, raw artwork bytes).
//!
//! Layout:
//! - `<storage>/cache/covers/<content_hash>` - the resized JPEG, filename = its
//!   own SHA1 (the client etag). Unchanged from the shipped plugin, so existing
//!   cover files are reused as-is.
//! - The album_key -> content_hash index and the last-check timestamp now live
//!   in the shared `mbrc.redb` ([`crate::store`]), replacing the old
//!   `cache/state.json`. A one-time import (`Db::migrate_cover_state`) brings a
//!   shipped `state.json` across so an existing built cache survives the upgrade.
//!
//! One deliberate change from C#: track modification times cross as unix seconds
//! (`i64`), not display strings, so the core needs no date parser - the C# leaf
//! provider does the conversion.

use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::RwLock;
use std::time::{Instant, SystemTime, UNIX_EPOCH};

use redb::{Durability, ReadableTable};

use super::{resize_to_jpeg, sha1_hex, CACHE_SIZE};
use crate::store::{Db, COVER_COVERS, COVER_META, LAST_CHECK};

/// One album's identity ingredients, provided by the host: the album key, a
/// representative track path (source of the artwork), and that file's mod time
/// (unix seconds) used to decide whether a cached cover is still valid.
#[derive(Debug, Clone)]
pub struct AlbumIdentity {
    pub key: String,
    pub path: String,
    pub modified: i64,
}

/// Per-cover timing breakdown for a from-scratch build, so the fetch (FFI round
/// trip to the host for raw artwork) can be told apart from the store (decode +
/// resize + JPEG encode + write). Milliseconds throughout. Filled by
/// [`CoverStore::build`] and logged as a summary by the caller; the slowest
/// single cover is kept for a quick "what stalled" pointer.
#[derive(Debug, Default, Clone)]
pub struct BuildStats {
    /// Albums that had no cached cover at the start of the build.
    pub attempted: usize,
    /// Covers successfully resized and written this build.
    pub stored: usize,
    /// Albums whose track returned no artwork (skipped, not a failure).
    pub no_art: usize,
    /// Covers whose store step errored (decode/encode/write).
    pub failed: usize,
    /// Total time spent fetching raw artwork over the FFI, in milliseconds.
    /// Single-threaded (the producer), so this is also wall-clock for fetch.
    pub fetch_ms: u128,
    /// Total CPU time spent decoding + resizing + encoding + writing, summed
    /// across worker threads - so with a parallel build it exceeds the wall-clock
    /// spent storing. Compare against `build_ms` (wall-clock) to see the speedup.
    pub store_ms: u128,
    /// The slowest single cover's total (fetch + store) time, in milliseconds.
    pub slowest_ms: u128,
    /// The slowest single cover's track path.
    pub slowest_path: String,
}

pub struct CoverStore {
    storage_path: PathBuf,
    /// The shared redb store, holding the durable album_key -> content_hash index
    /// and the last-check timestamp. In-memory maps below are the hot read cache
    /// loaded from here at `warm_up`.
    db: Db,
    /// album_key -> content_hash (the cached, resized cover's SHA1).
    covers: RwLock<HashMap<String, String>>,
    /// album_key -> representative track path (artwork source). Derived each
    /// `warm_up`; never persisted (rebuilt from the host's album list).
    paths: RwLock<HashMap<String, String>>,
    building: AtomicBool,
}

impl CoverStore {
    pub fn new(db: Db, storage_path: impl Into<PathBuf>) -> Self {
        Self {
            storage_path: storage_path.into(),
            db,
            covers: RwLock::new(HashMap::new()),
            paths: RwLock::new(HashMap::new()),
            building: AtomicBool::new(false),
        }
    }

    /// Test helper: open a fresh `Db` rooted at `dir` and build a store on it.
    /// Production shares one `Db` across the stores via `new`.
    #[cfg(test)]
    pub fn open_at(dir: impl AsRef<std::path::Path>) -> Self {
        let dir = dir.as_ref();
        Self::new(
            Db::open(dir.to_str().unwrap_or_default()),
            dir.to_path_buf(),
        )
    }

    fn cache_dir(&self) -> PathBuf {
        self.storage_path.join("cache")
    }
    fn covers_dir(&self) -> PathBuf {
        self.cache_dir().join("covers")
    }
    fn cover_file(&self, content_hash: &str) -> PathBuf {
        self.covers_dir().join(content_hash)
    }

    /// Whether a cache build is in progress (serves `librarycovercachebuildstatus`).
    pub fn is_building(&self) -> bool {
        self.building.load(Ordering::Acquire)
    }

    /// The cached content hash for an album key, if any.
    pub fn hash_for(&self, key: &str) -> Option<String> {
        self.read_covers().get(key).cloned()
    }

    /// Number of albums with a cached cover (for the "Done. N cached" message).
    pub fn cached_count(&self) -> usize {
        self.read_covers().len()
    }

    /// The representative track path for an album key, if known.
    pub fn path_for(&self, key: &str) -> Option<String> {
        self.read_paths().get(key).cloned()
    }

    /// The album keys currently known (from the last warm-up), sorted for stable
    /// paging.
    pub fn keys(&self) -> Vec<String> {
        let mut keys: Vec<String> = self.read_paths().keys().cloned().collect();
        keys.sort();
        keys
    }

    /// Read a cached cover's JPEG bytes by content hash.
    pub fn read_cover_bytes(&self, content_hash: &str) -> Option<Vec<u8>> {
        std::fs::read(self.cover_file(content_hash)).ok()
    }

    /// Read a cached cover as base64 (the wire `cover` field), by content hash.
    pub fn read_cover_base64(&self, content_hash: &str) -> Option<String> {
        self.read_cover_bytes(content_hash)
            .map(|bytes| super::to_base64(&bytes))
    }

    /// Cache one album's cover on demand: resize+hash+store the raw artwork, map
    /// `key -> hash`, and return the hash. Used to fill a single-cover request
    /// that missed the pre-built cache (mirrors C# `GetAlbumCover`'s lazy path).
    pub fn cache_cover(&self, key: &str, raw: &[u8]) -> Result<String, String> {
        let hash = self.store_cover(raw)?;
        self.write_covers().insert(key.to_string(), hash.clone());
        Ok(hash)
    }

    /// Resize raw artwork to the cache thumbnail, hash it, write the file, and
    /// return the content hash. The file name IS the hash (content-addressed).
    fn store_cover(&self, raw: &[u8]) -> Result<String, String> {
        let jpeg = resize_to_jpeg(raw, CACHE_SIZE, CACHE_SIZE)?;
        let hash = sha1_hex(&jpeg);
        let dir = self.covers_dir();
        std::fs::create_dir_all(&dir).map_err(|e| format!("create covers dir: {e}"))?;
        std::fs::write(self.cover_file(&hash), &jpeg).map_err(|e| format!("write cover: {e}"))?;
        Ok(hash)
    }

    /// Warm the cache from the host's album list: record the key->path map, then
    /// load `state.json` and keep each cached cover whose track file has NOT been
    /// modified since the last check (mirrors C# `WarmUpCache`). Covers for
    /// modified or unknown tracks are dropped so `build` refetches them.
    pub fn warm_up(&self, albums: &[AlbumIdentity]) {
        let path_map: HashMap<String, String> = albums
            .iter()
            .map(|a| (a.key.clone(), a.path.clone()))
            .collect();
        *self.write_paths() = path_map;

        let (persisted, last_check) = self.load_state();
        let mut covers = self.write_covers();
        covers.clear();
        for a in albums {
            if let Some(hash) = persisted.get(&a.key) {
                // Keep only if the track predates the last cache check.
                if a.modified < last_check {
                    covers.insert(a.key.clone(), hash.clone());
                }
            }
        }
    }

    /// Build missing covers: for every known album without a cached cover, fetch
    /// its raw artwork via `fetch_raw`, resize+hash+store it, and map it. Then
    /// prune orphaned files (on disk but unreferenced) and persist. Single-flight:
    /// concurrent calls return immediately while one build runs.
    ///
    /// Returns a [`BuildStats`] breaking the wall-clock into fetch (FFI) vs store
    /// (decode/resize/encode/write) so the caller can log where the time went.
    /// When `verbose` is set, each cover's timing is logged at info as it is
    /// built (opt-in: 1400+ lines), otherwise only the caller's summary is emitted.
    /// A skipped concurrent call returns the default (all-zero) stats.
    pub fn build<F>(&self, fetch_raw: F, verbose: bool) -> BuildStats
    where
        F: Fn(&str) -> Option<Vec<u8>>,
    {
        if self.building.swap(true, Ordering::AcqRel) {
            return BuildStats::default(); // a build is already running
        }
        let stats = self.build_inner(fetch_raw, verbose);
        self.building.store(false, Ordering::Release);
        stats
    }

    fn build_inner<F>(&self, fetch_raw: F, verbose: bool) -> BuildStats
    where
        F: Fn(&str) -> Option<Vec<u8>>,
    {
        // Snapshot the (key, path) pairs that still need a cover. An album needs
        // one when it has no state entry OR its recorded cover file is gone from
        // disk - so the cache self-heals after a file is lost (manual delete,
        // crash mid-build, partial clear) instead of trusting state.json forever
        // and leaving that album permanently blank.
        let missing: Vec<(String, String)> = {
            let covers = self.read_covers();
            self.read_paths()
                .iter()
                .filter(|(k, _)| match covers.get(*k) {
                    None => true,
                    Some(hash) => !self.cover_file(hash).exists(),
                })
                .map(|(k, p)| (k.clone(), p.clone()))
                .collect()
        };

        let mut stats = BuildStats {
            attempted: missing.len(),
            ..BuildStats::default()
        };

        // The store step (decode + resize + JPEG encode + write) is CPU-bound and
        // ~90% of per-cover time, so it parallelizes across cores. The fetch step
        // is an FFI callback into the host's MusicBee API, whose thread-safety we
        // don't control - so it stays on this single thread (the producer) and
        // only the CPU work fans out to workers. A bounded queue caps how many
        // decoded images sit in memory at once.
        let workers = std::thread::available_parallelism()
            .map(|n| n.get())
            .unwrap_or(4)
            .clamp(1, 8);

        // (key, path, raw artwork bytes, fetch_ms) handed from producer to workers.
        let (tx, rx) =
            std::sync::mpsc::sync_channel::<(String, String, Vec<u8>, u128)>(workers * 2);
        let rx = std::sync::Mutex::new(rx);

        std::thread::scope(|scope| {
            let handles: Vec<_> = (0..workers)
                .map(|_| {
                    let rx = &rx;
                    scope.spawn(move || {
                        let mut local = BuildStats::default();
                        loop {
                            // Take the receiver lock only long enough to pull one
                            // item; the store work runs without holding it.
                            let item = rx.lock().unwrap().recv();
                            let Ok((key, path, raw, fetch_ms)) = item else {
                                break; // producer dropped the sender: queue drained
                            };

                            let store_start = Instant::now();
                            let result = self.store_cover(&raw);
                            let store_ms = store_start.elapsed().as_millis();
                            local.store_ms += store_ms;

                            match result {
                                Ok(hash) => {
                                    self.write_covers().insert(key, hash);
                                    local.stored += 1;
                                }
                                Err(e) => {
                                    local.failed += 1;
                                    tracing::debug!(%path, error = %e, "cover build: store failed");
                                }
                            }

                            let total_ms = fetch_ms + store_ms;
                            if total_ms > local.slowest_ms {
                                local.slowest_ms = total_ms;
                                local.slowest_path = path.clone();
                            }
                            if verbose {
                                // Info level so the trace shows regardless of build
                                // profile once the operator turns on debug logging -
                                // it is already gated here.
                                tracing::info!(
                                    %path,
                                    fetch_ms,
                                    store_ms,
                                    bytes = raw.len(),
                                    "cover build: timing"
                                );
                            }
                        }
                        local
                    })
                })
                .collect();

            // Producer: fetch each cover's raw artwork sequentially (single FFI
            // thread) and feed the queue. `send` blocks when the bound is hit,
            // giving back-pressure so memory stays bounded.
            for (key, path) in missing {
                let fetch_start = Instant::now();
                let raw = fetch_raw(&path);
                let fetch_ms = fetch_start.elapsed().as_millis();
                stats.fetch_ms += fetch_ms;
                match raw {
                    Some(raw) => {
                        let _ = tx.send((key, path, raw, fetch_ms));
                    }
                    None => stats.no_art += 1, // no artwork for this track
                }
            }
            drop(tx); // close the queue so workers exit once it drains

            for handle in handles {
                let local = handle.join().unwrap_or_default();
                stats.stored += local.stored;
                stats.failed += local.failed;
                stats.store_ms += local.store_ms;
                if local.slowest_ms > stats.slowest_ms {
                    stats.slowest_ms = local.slowest_ms;
                    stats.slowest_path = local.slowest_path;
                }
            }
        });

        self.prune_orphans();
        self.persist();
        stats
    }

    /// Delete cover files that are no longer referenced by any album key.
    fn prune_orphans(&self) {
        let referenced: std::collections::HashSet<String> =
            self.read_covers().values().cloned().collect();
        let Ok(entries) = std::fs::read_dir(self.covers_dir()) else {
            return; // no covers dir yet
        };
        for entry in entries.flatten() {
            let name = entry.file_name();
            let Some(name) = name.to_str() else { continue };
            if referenced.contains(name) {
                continue;
            }
            if let Err(e) = std::fs::remove_file(entry.path()) {
                tracing::debug!(file = name, error = %e, "cover prune: delete failed");
            }
        }
    }

    /// Load the persisted album_key -> content_hash index and last-check time
    /// from redb. A missing table (fresh store) or disabled `Db` yields an empty
    /// map and a zero timestamp.
    fn load_state(&self) -> (HashMap<String, String>, i64) {
        let covers = self
            .db
            .read(|txn| {
                let table = match txn.open_table(COVER_COVERS) {
                    Ok(t) => t,
                    Err(redb::TableError::TableDoesNotExist(_)) => return Ok(HashMap::new()),
                    Err(e) => return Err(e.into()),
                };
                let mut map = HashMap::new();
                for entry in table.iter()? {
                    let (k, v) = entry?;
                    map.insert(k.value().to_string(), v.value().to_string());
                }
                Ok(map)
            })
            .unwrap_or_default();
        let last_check = self
            .db
            .read(|txn| {
                let table = match txn.open_table(COVER_META) {
                    Ok(t) => t,
                    Err(redb::TableError::TableDoesNotExist(_)) => return Ok(0),
                    Err(e) => return Err(e.into()),
                };
                Ok(table.get(LAST_CHECK)?.map(|g| g.value()).unwrap_or(0))
            })
            .unwrap_or(0);
        (covers, last_check)
    }

    /// Persist the in-memory covers map + LastCheck=now to redb in one durable
    /// transaction. The covers table is rebuilt wholesale (delete + reinsert) so
    /// keys dropped by warm-up/prune don't linger - the same whole-map semantics
    /// the old `state.json` rewrite had, but crash-safe via redb's commit.
    fn persist(&self) {
        let covers = self.read_covers().clone();
        let last = now_unix_secs();
        self.db.write(Durability::Immediate, |txn| {
            txn.delete_table(COVER_COVERS)?;
            {
                let mut table = txn.open_table(COVER_COVERS)?;
                for (key, hash) in &covers {
                    table.insert(key.as_str(), hash.as_str())?;
                }
            }
            {
                let mut meta = txn.open_table(COVER_META)?;
                meta.insert(LAST_CHECK, last)?;
            }
            Ok(())
        });
    }

    fn read_covers(&self) -> std::sync::RwLockReadGuard<'_, HashMap<String, String>> {
        self.covers.read().unwrap_or_else(|e| e.into_inner())
    }
    fn write_covers(&self) -> std::sync::RwLockWriteGuard<'_, HashMap<String, String>> {
        self.covers.write().unwrap_or_else(|e| e.into_inner())
    }
    fn read_paths(&self) -> std::sync::RwLockReadGuard<'_, HashMap<String, String>> {
        self.paths.read().unwrap_or_else(|e| e.into_inner())
    }
    fn write_paths(&self) -> std::sync::RwLockWriteGuard<'_, HashMap<String, String>> {
        self.paths.write().unwrap_or_else(|e| e.into_inner())
    }

    #[cfg(test)]
    fn state_last_check(&self) -> i64 {
        self.load_state().1
    }
}

fn now_unix_secs() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::cover::test_jpeg_bytes as jpeg_bytes;

    /// A unique temp dir per test name (tests run in parallel), cleaned first,
    /// plus a shared `Db` on it. redb takes an exclusive file lock, so a test
    /// that opens a second store at the same dir must reuse this handle (Arc
    /// clone) - which also mirrors production, where one `Db` is shared.
    fn temp_storage(name: &str) -> (Db, PathBuf) {
        let dir = std::env::temp_dir().join(format!("mbrc-cover-store-{name}"));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let db = Db::open(dir.to_str().unwrap());
        (db, dir)
    }

    #[test]
    fn build_stores_hashes_and_persists_state() {
        let (db, dir) = temp_storage("build");
        let store = CoverStore::new(db.clone(), &dir);
        store.warm_up(&[
            AlbumIdentity {
                key: "alb1".into(),
                path: "/a.mp3".into(),
                modified: 0,
            },
            AlbumIdentity {
                key: "alb2".into(),
                path: "/b.mp3".into(),
                modified: 0,
            },
        ]);

        let art = jpeg_bytes(300, 300);
        let stats = store.build(
            |path| {
                if path.ends_with(".mp3") {
                    Some(art.clone())
                } else {
                    None
                }
            },
            false,
        );

        // The stats reflect what was attempted/stored this build.
        assert_eq!(stats.attempted, 2);
        assert_eq!(stats.stored, 2);
        assert_eq!(stats.failed, 0);

        // Both albums got a content hash, the files exist, and state persisted.
        let h1 = store.hash_for("alb1").expect("alb1 cached");
        assert_eq!(h1.len(), 40);
        assert!(store.read_cover_bytes(&h1).is_some());
        assert!(store.state_last_check() > 0, "LastCheck persisted to redb");
        assert!(!store.is_building());

        // A fresh store warmed from the same identities reuses the cached hash
        // (track mod-time 0 predates the just-written LastCheck).
        let store2 = CoverStore::new(db.clone(), &dir);
        store2.warm_up(&[AlbumIdentity {
            key: "alb1".into(),
            path: "/a.mp3".into(),
            modified: 0,
        }]);
        assert_eq!(store2.hash_for("alb1"), Some(h1));
    }

    #[test]
    fn build_regenerates_a_cover_whose_file_was_deleted() {
        // A state entry alone must not be trusted: if the cover file is gone, the
        // next build re-fetches and re-writes it (self-healing cache), instead of
        // skipping the album and leaving it permanently blank.
        let (db, dir) = temp_storage("selfheal");
        let store = CoverStore::new(db.clone(), &dir);
        store.warm_up(&[AlbumIdentity {
            key: "alb1".into(),
            path: "/a.mp3".into(),
            modified: 0,
        }]);

        let art = jpeg_bytes(200, 200);
        let first = store.build(|_| Some(art.clone()), false);
        assert_eq!(first.stored, 1);
        let hash = store.hash_for("alb1").expect("alb1 cached");

        // Simulate a lost cover file (manual clear, crash mid-write, etc.) while
        // the state entry survives.
        std::fs::remove_file(store.cover_file(&hash)).unwrap();
        assert!(store.read_cover_bytes(&hash).is_none());

        // A fresh store loads the state, but the build still re-attempts alb1
        // because its file is missing, and restores it.
        let store2 = CoverStore::new(db.clone(), &dir);
        store2.warm_up(&[AlbumIdentity {
            key: "alb1".into(),
            path: "/a.mp3".into(),
            modified: 0,
        }]);
        let second = store2.build(|_| Some(art.clone()), false);
        assert_eq!(second.attempted, 1, "missing file should be re-attempted");
        assert_eq!(second.stored, 1);
        assert!(store2.read_cover_bytes(&hash).is_some());
    }

    #[test]
    fn warm_up_drops_covers_for_modified_tracks() {
        let (db, dir) = temp_storage("modified");
        let store = CoverStore::new(db.clone(), &dir);
        store.warm_up(&[AlbumIdentity {
            key: "alb1".into(),
            path: "/a.mp3".into(),
            modified: 0,
        }]);
        let art = jpeg_bytes(200, 200);
        store.build(|_| Some(art.clone()), false);
        let last_check = store.state_last_check();
        assert!(store.hash_for("alb1").is_some());

        // Re-warm with a track modified AFTER the last check -> cover dropped.
        let store2 = CoverStore::new(db.clone(), &dir);
        store2.warm_up(&[AlbumIdentity {
            key: "alb1".into(),
            path: "/a.mp3".into(),
            modified: last_check + 1000,
        }]);
        assert_eq!(store2.hash_for("alb1"), None);
    }

    #[test]
    fn build_prunes_orphaned_files() {
        let (db, dir) = temp_storage("prune");
        let store = CoverStore::new(db, &dir);
        // Plant an orphan file in the covers dir.
        std::fs::create_dir_all(dir.join("cache").join("covers")).unwrap();
        let orphan = dir.join("cache").join("covers").join("deadbeef");
        std::fs::write(&orphan, b"stale").unwrap();

        store.warm_up(&[AlbumIdentity {
            key: "alb1".into(),
            path: "/a.mp3".into(),
            modified: 0,
        }]);
        store.build(|_| Some(jpeg_bytes(150, 150)), false);

        assert!(!orphan.exists(), "orphaned cover file should be pruned");
    }
}
