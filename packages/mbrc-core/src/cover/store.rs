//! On-disk album cover cache (the Rust port of C# `CoverCache` + the cache half
//! of `CoverService`).
//!
//! The core owns identities, resizing, storage, and serving; the C# host only
//! provides raw ingredients (album list, track paths, mod times, raw artwork
//! bytes).
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
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::mpsc::Receiver;
use std::sync::{Condvar, Mutex, RwLock};
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use redb::{Durability, ReadableTable};

use super::source::Source;
use super::{CACHE_SIZE, decode_cost, resize_cover, sha1_hex};
use crate::store::{COVER_COVERS, COVER_META, COVER_NO_ART, COVER_SIZE, Db, LAST_CHECK};

/// The most decode workers a build runs.
///
/// Fetching from MusicBee is one thread at about 25 ms a cover, which two
/// workers already keep up with for all but large progressive JPEGs; more only
/// multiplies the memory those hold at once.
const MAX_WORKERS: usize = 4;

/// The memory all workers' decodes may hold at once, by [`decode_cost`].
const DECODE_BUDGET: usize = 64 * 1024 * 1024;

/// How long "this album has no artwork" is believed before it is asked again.
///
/// A folder image can be added without touching the track, so the track's
/// modification time alone would never retry it.
const NO_ART_RETRY_SECS: i64 = 7 * 24 * 60 * 60;

/// How many newly stored covers are saved together during a build.
///
/// A build that dies keeps everything up to its last checkpoint, so the next
/// start resumes rather than starting over and dying at the same place.
const CHECKPOINT_EVERY: usize = 200;

/// How often a build reports its progress at INFO: every this many albums...
const PROGRESS_EVERY: usize = 250;

/// ...or this long, whichever comes first.
const PROGRESS_INTERVAL: Duration = Duration::from_secs(10);

/// Where a build has got to, for its progress report.
#[derive(Debug, Clone, Copy)]
pub struct BuildProgress<'a> {
    /// Albums fetched so far.
    pub done: usize,
    pub total: usize,
    pub stored: usize,
    pub failed: usize,
    /// The album being fetched: its representative track.
    pub path: &'a str,
}

/// What fetching one album's artwork found.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Artwork {
    Found(Vec<u8>),
    /// The album has no artwork; remembered, so it is not asked again every build.
    Missing,
    /// The fetch itself failed; asked again next build.
    Unavailable,
}

/// What the build's producer learned about one album's artwork.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Lookup {
    /// Settled already: the bytes, no artwork, or a failed fetch.
    Done(Artwork),
    /// For a worker to read; MusicBee is asked for the bytes only if that fails.
    Read(Source),
}

/// How many albums at the start of a build are read both ways at DEBUG, to
/// check that the core picks the same picture MusicBee would.
const PARITY_SAMPLE: usize = 50;

impl From<Option<Vec<u8>>> for Artwork {
    fn from(raw: Option<Vec<u8>>) -> Self {
        raw.map_or(Self::Missing, Self::Found)
    }
}

/// One album's identity ingredients, provided by the host.
///
/// The album key, a representative track path (the artwork source), and that
/// file's mod time in unix seconds, which decides whether a cached cover is
/// still valid.
#[derive(Debug, Clone)]
pub struct AlbumIdentity {
    pub key: String,
    pub path: String,
    pub modified: i64,
}

/// Per-cover timing breakdown for a from-scratch build, so the fetch (FFI round
/// trip to the host for raw artwork) can be told apart from the store (decode +
/// resize + JPEG encode + write).
///
/// Milliseconds throughout. Filled by [`CoverStore::build`] and logged as a
/// summary by the caller; the slowest single cover is kept for a quick "what
/// stalled" pointer.
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
    /// Time the workers spent reading artwork the core reads itself, summed
    /// across them: the share of a build that waits on the disk, not the CPU.
    pub read_ms: u128,
    /// Total CPU time spent decoding + resizing + encoding + writing, summed
    /// across worker threads - so with a parallel build it exceeds the wall-clock
    /// spent storing. Compare against `build_ms` (wall-clock) to see the speedup.
    pub store_ms: u128,
    /// The slowest single cover's total (fetch, read and store) time, in milliseconds.
    pub slowest_ms: u128,
    /// The slowest single cover's track path.
    pub slowest_path: String,
    /// The build stopped early because it was asked to - the core is shutting
    /// down. Not a failure: what was built is kept and the next build resumes.
    pub stopped: bool,
    /// The largest source image seen, as width and height.
    pub largest: (u32, u32),
    /// Covers whose artwork the core read itself rather than through MusicBee.
    pub read_in_core: usize,
    /// Albums the core could not read, fetched through MusicBee instead.
    pub fell_back: usize,
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
    /// album_key -> when it was found to have no artwork (unix seconds).
    no_art: RwLock<HashMap<String, i64>>,
    building: AtomicBool,
}

impl CoverStore {
    pub fn new(db: Db, storage_path: impl Into<PathBuf>) -> Self {
        Self {
            storage_path: storage_path.into(),
            db,
            covers: RwLock::new(HashMap::new()),
            paths: RwLock::new(HashMap::new()),
            no_art: RwLock::new(HashMap::new()),
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

    /// Reads a cached cover's JPEG bytes by content hash.
    pub fn read_cover_bytes(&self, content_hash: &str) -> Option<Vec<u8>> {
        std::fs::read(self.cover_file(content_hash)).ok()
    }

    /// Reads a cached cover as base64 (the wire `cover` field), by content hash.
    pub fn read_cover_base64(&self, content_hash: &str) -> Option<String> {
        self.read_cover_bytes(content_hash)
            .map(|bytes| super::to_base64(&bytes))
    }

    /// Caches one album's cover on demand: resize+hash+store the raw artwork, map
    /// `key -> hash`, and return the hash. Used to fill a single-cover request
    /// that missed the pre-built cache (mirrors C# `GetAlbumCover`'s lazy path).
    ///
    /// # Errors
    /// The artwork does not resize, or the cover file cannot be written.
    pub fn cache_cover(&self, key: &str, raw: &[u8]) -> Result<String, String> {
        let (hash, _) = self.store_cover(raw)?;
        self.write_covers().insert(key.to_string(), hash.clone());
        Ok(hash)
    }

    /// Resizes raw artwork to the cache thumbnail, hashes it, writes the file,
    /// and returns the content hash with the source's size. The file name IS the
    /// hash (content-addressed).
    fn store_cover(&self, raw: &[u8]) -> Result<(String, (u32, u32)), String> {
        let resized = resize_cover(raw, CACHE_SIZE, CACHE_SIZE)?;
        let hash = sha1_hex(&resized.jpeg);
        let dir = self.covers_dir();
        std::fs::create_dir_all(&dir).map_err(|e| format!("create covers dir: {e}"))?;
        std::fs::write(self.cover_file(&hash), &resized.jpeg)
            .map_err(|e| format!("write cover: {e}"))?;
        tracing::debug!(
            source = %format_args!("{}x{}", resized.source.0, resized.source.1),
            reduced = resized.reduced,
            bytes = raw.len(),
            "cover stored"
        );
        Ok((hash, resized.source))
    }

    /// Warms the cache from the host's album list: record the key->path map,
    /// then keep each cached cover whose track file has NOT been modified since
    /// the last check. Covers for modified, unknown or removed albums are
    /// dropped so `build` refetches them, and `prune_orphans` deletes a
    /// content-hashed file once no key references it.
    ///
    /// A change to [`CACHE_SIZE`] keeps nothing: the source files are unchanged,
    /// so every cover would otherwise survive at its original size.
    pub fn warm_up(&self, albums: &[AlbumIdentity]) {
        let path_map: HashMap<String, String> = albums
            .iter()
            .map(|a| (a.key.clone(), a.path.clone()))
            .collect();
        *self.write_paths() = path_map;

        let (persisted, last_check) = self.load_state();
        let now = now_unix_secs();
        let modified: HashMap<&str, i64> = albums
            .iter()
            .map(|a| (a.key.as_str(), a.modified))
            .collect();
        let no_art: HashMap<String, i64> = self
            .load_no_art()
            .into_iter()
            .filter(|(key, at)| {
                now - at < NO_ART_RETRY_SECS
                    && modified.get(key.as_str()).is_some_and(|m| *m < last_check)
            })
            .collect();
        *self.no_art.write().unwrap_or_else(|e| e.into_inner()) = no_art;
        let resized = self.stored_cover_size() != Some(CACHE_SIZE);
        let mut covers = self.write_covers();
        covers.clear();
        if resized {
            self.store_cover_size();
            return;
        }
        for a in albums {
            if let Some(hash) = persisted.get(&a.key) {
                // Keep only if the track predates the last cache check.
                if a.modified < last_check {
                    covers.insert(a.key.clone(), hash.clone());
                }
            }
        }
    }

    /// The size the stored covers were built at, if one has been recorded.
    fn stored_cover_size(&self) -> Option<u32> {
        self.db
            .read(|txn| {
                let table = match txn.open_table(COVER_META) {
                    Ok(t) => t,
                    Err(redb::TableError::TableDoesNotExist(_)) => return Ok(None),
                    Err(e) => return Err(e.into()),
                };
                Ok(table.get(COVER_SIZE)?.map(|g| g.value() as u32))
            })
            .flatten()
    }

    fn store_cover_size(&self) {
        self.db.write(Durability::Immediate, |txn| {
            let mut meta = txn.open_table(COVER_META)?;
            meta.insert(COVER_SIZE, i64::from(CACHE_SIZE))?;
            Ok(())
        });
    }

    /// Forgets which albums were found to have no artwork, so the next build
    /// asks again. A manual rebuild does this; it is what a user reaches for
    /// after adding a folder image.
    pub fn forget_missing_art(&self) {
        self.no_art
            .write()
            .unwrap_or_else(|e| e.into_inner())
            .clear();
    }

    /// Builds missing covers: fetch each album's artwork, resize+hash+store it,
    /// then prune orphaned files and persist.
    ///
    /// Single-flight, so a concurrent call returns the default all-zero stats
    /// immediately rather than building twice. `verbose` logs a timing line per
    /// cover at info, which is opt-in because a full library is 1400+ lines. The
    /// returned [`BuildStats`] splits the wall-clock into fetch and store.
    pub fn build<F, A>(&self, fetch_raw: F, verbose: bool) -> BuildStats
    where
        F: Fn(&str) -> A,
        A: Into<Artwork>,
    {
        self.build_until(fetch_raw, verbose, &|| false)
    }

    /// As [`Self::build`], but stops early when `stop` says so.
    ///
    /// Checked between albums, which is the only place it can be: a fetch is one
    /// blocking call into MusicBee and a store is one image. What has been built
    /// is still pruned and persisted, and the next build picks up the rest -
    /// "missing" is recomputed from what is actually on disk every time, so a
    /// half-finished build is a resumable one rather than a broken cache.
    pub fn build_until<F, A>(
        &self,
        fetch_raw: F,
        verbose: bool,
        stop: &dyn Fn() -> bool,
    ) -> BuildStats
    where
        F: Fn(&str) -> A,
        A: Into<Artwork>,
    {
        self.build_reporting(fetch_raw, verbose, stop, &|_| {})
    }

    /// As [`Self::build_until`], also logging progress at INFO and handing each
    /// report to `progress`, so the caller can note where the build has got to.
    pub fn build_reporting<F, A>(
        &self,
        fetch_raw: F,
        verbose: bool,
        stop: &dyn Fn() -> bool,
        progress: &dyn Fn(&BuildProgress<'_>),
    ) -> BuildStats
    where
        F: Fn(&str) -> A,
        A: Into<Artwork>,
    {
        self.build_located(
            |path| Lookup::Done(fetch_raw(path).into()),
            |path| fetch_raw(path).into(),
            verbose,
            stop,
            progress,
        )
    }

    /// As [`Self::build_reporting`], asking `locate` where each album's artwork
    /// is and letting the workers read it; `fetch` gets the bytes from MusicBee
    /// for what they cannot read.
    pub fn build_located<L, F>(
        &self,
        locate: L,
        fetch: F,
        verbose: bool,
        stop: &dyn Fn() -> bool,
        progress: &dyn Fn(&BuildProgress<'_>),
    ) -> BuildStats
    where
        L: Fn(&str) -> Lookup,
        F: Fn(&str) -> Artwork,
    {
        if self.building.swap(true, Ordering::AcqRel) {
            return BuildStats::default(); // a build is already running
        }
        let stats = self.build_inner(&locate, &fetch, verbose, stop, progress);
        self.building.store(false, Ordering::Release);
        stats
    }

    /// The build itself, split across a producer and a pool of workers.
    ///
    /// Fetching is an FFI callback whose thread-safety the host controls, so it
    /// stays on this thread as the producer; storing (decode, resize, encode,
    /// write) is CPU-bound and ~90% of per-cover time, so it fans out. The queue
    /// is bounded, which caps how many decoded images are in memory at once.
    /// New covers are saved every [`CHECKPOINT_EVERY`], not only at the end.
    fn build_inner(
        &self,
        locate: &dyn Fn(&str) -> Lookup,
        fetch: &dyn Fn(&str) -> Artwork,
        verbose: bool,
        stop: &dyn Fn() -> bool,
        progress: &dyn Fn(&BuildProgress<'_>),
    ) -> BuildStats {
        // Self-healing: an entry whose file is gone still counts as missing.
        let missing: Vec<(String, String)> = {
            let covers = self.read_covers();
            let no_art = self.no_art.read().unwrap_or_else(|e| e.into_inner());
            self.read_paths()
                .iter()
                .filter(|(k, _)| !no_art.contains_key(*k))
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
        // Stale entries leave the table now, so a checkpoint cannot make them look valid.
        self.persist_at(now_unix_secs());
        let shared = Shared::default();
        let mut parity = Parity::new();

        let workers = std::thread::available_parallelism()
            .map(|n| n.get())
            .unwrap_or(MAX_WORKERS)
            .clamp(1, MAX_WORKERS);

        let (tx, rx) = std::sync::mpsc::sync_channel::<Fetched>(workers * 2);
        let rx = std::sync::Mutex::new(rx);

        std::thread::scope(|scope| {
            let handles: Vec<_> = (0..workers)
                .map(|_| {
                    let (rx, shared) = (&rx, &shared);
                    scope.spawn(move || self.work(rx, shared, verbose))
                })
                .collect();

            // Producer: sequential FFI fetches feed the queue, and `send`
            // blocking at the bound is the back-pressure.
            let total = missing.len();
            let mut last_report = Instant::now();
            for (done, (key, path)) in missing.into_iter().enumerate() {
                if stop() {
                    stats.stopped = true;
                    break;
                }
                if done > 0
                    && (done % PROGRESS_EVERY == 0 || last_report.elapsed() >= PROGRESS_INTERVAL)
                {
                    last_report = Instant::now();
                    report_progress(
                        &BuildProgress {
                            done,
                            total,
                            stored: shared.stored.load(Ordering::Relaxed),
                            failed: shared.failed.load(Ordering::Relaxed),
                            path: &path,
                        },
                        shared.largest(),
                        progress,
                    );
                }
                let fetch_start = Instant::now();
                let job = match locate(&path) {
                    Lookup::Read(source) => Some(parity.check(source, &path, fetch)),
                    Lookup::Done(artwork) => self.settle(artwork, &key, &path, &shared, &mut stats),
                };
                let fetch_ms = fetch_start.elapsed().as_millis();
                stats.fetch_ms += fetch_ms;
                if let Some(job) = job {
                    let _ = tx.send((key, path, job, fetch_ms));
                }
            }
            drop(tx); // close the queue so workers exit once it drains

            for handle in handles {
                let local = handle.join().unwrap_or_default();
                stats.stored += local.stored;
                stats.failed += local.failed;
                stats.read_ms += local.read_ms;
                stats.store_ms += local.store_ms;
                if local.slowest_ms > stats.slowest_ms {
                    stats.slowest_ms = local.slowest_ms;
                    stats.slowest_path = local.slowest_path;
                }
            }
        });
        stats.largest = shared.largest();
        stats.read_in_core = shared.read_in_core.load(Ordering::Relaxed);
        self.fall_back(fetch, stop, &shared, &mut stats);
        parity.report();

        self.prune_orphans();
        self.persist();
        stats
    }

    /// One worker: stores covers off the queue until the producer closes it.
    fn work(&self, rx: &Mutex<Receiver<Fetched>>, shared: &Shared, verbose: bool) -> BuildStats {
        below_normal_priority();
        let mut local = BuildStats::default();
        loop {
            // Held only to pull one item, never across the store.
            let item = rx.lock().expect("cover queue mutex poisoned").recv();
            let Ok((key, path, job, fetch_ms)) = item else {
                break; // producer dropped the sender: queue drained
            };
            let read_start = Instant::now();
            let raw = match job {
                Job::Bytes(raw) => raw,
                Job::Read(source) => match source.read() {
                    Ok(raw) => {
                        shared.read_in_core.fetch_add(1, Ordering::Relaxed);
                        raw
                    }
                    Err(e) => {
                        tracing::debug!(%path, error = %e, "cover build: reading artwork failed; asking MusicBee");
                        shared.lock_fallback().push((key, path));
                        continue;
                    }
                },
            };
            let read_ms = read_start.elapsed().as_millis();
            local.read_ms += read_ms;

            let permit = shared.budget.reserve(decode_cost(&raw, CACHE_SIZE));
            let store_start = Instant::now();
            let result = self.store_cover(&raw);
            let store_ms = store_start.elapsed().as_millis();
            drop(permit);
            local.store_ms += store_ms;

            match result {
                Ok((hash, source)) => {
                    self.write_covers().insert(key.clone(), hash.clone());
                    local.stored += 1;
                    shared.stored.fetch_add(1, Ordering::Relaxed);
                    shared.note_largest(source);
                    self.checkpoint(&shared.pending, |pending| pending.covers.push((key, hash)));
                }
                Err(e) => {
                    local.failed += 1;
                    shared.failed.fetch_add(1, Ordering::Relaxed);
                    tracing::debug!(%path, error = %e, "cover build: store failed");
                }
            }

            let total_ms = fetch_ms + read_ms + store_ms;
            if total_ms > local.slowest_ms {
                local.slowest_ms = total_ms;
                local.slowest_path = path.clone();
            }
            if verbose {
                // INFO, not DEBUG: `verbose` is already the gate.
                tracing::info!(
                    %path,
                    fetch_ms,
                    read_ms,
                    store_ms,
                    bytes = raw.len(),
                    "cover build: timing"
                );
            }
        }
        local
    }

    /// Handles an album the producer settled itself: queues its bytes for a
    /// worker, or records that it has no artwork or could not be fetched.
    fn settle(
        &self,
        artwork: Artwork,
        key: &str,
        path: &str,
        shared: &Shared,
        stats: &mut BuildStats,
    ) -> Option<Job> {
        match artwork {
            Artwork::Found(raw) => return Some(Job::Bytes(raw)),
            Artwork::Missing => {
                stats.no_art += 1;
                self.remember_no_art(&shared.pending, key.to_owned());
            }
            Artwork::Unavailable => {
                stats.failed += 1;
                tracing::debug!(%path, "cover build: artwork fetch failed");
            }
        }
        None
    }

    /// Fetches through MusicBee what the workers could not read, then stores it.
    ///
    /// On the producer's thread, after the workers have finished: MusicBee's
    /// callbacks stay on one thread, and a fallback is the exception.
    fn fall_back(
        &self,
        fetch: &dyn Fn(&str) -> Artwork,
        stop: &dyn Fn() -> bool,
        shared: &Shared,
        stats: &mut BuildStats,
    ) {
        let pending = std::mem::take(&mut *shared.lock_fallback());
        stats.fell_back = pending.len();
        for (key, path) in pending {
            if stop() {
                stats.stopped = true;
                break;
            }
            let Some(Job::Bytes(raw)) = self.settle(fetch(&path), &key, &path, shared, stats)
            else {
                continue;
            };
            match self.store_cover(&raw) {
                Ok((hash, _)) => {
                    self.write_covers().insert(key.clone(), hash.clone());
                    stats.stored += 1;
                    self.checkpoint(&shared.pending, |pending| pending.covers.push((key, hash)));
                }
                Err(e) => {
                    stats.failed += 1;
                    tracing::debug!(%path, error = %e, "cover build: store failed");
                }
            }
        }
    }

    /// Deletes cover files that are no longer referenced by any album key.
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

    /// Loads the persisted album_key -> content_hash index and last-check time
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

    /// Persists the in-memory covers map + LastCheck=now to redb in one durable
    /// transaction. The covers table is rebuilt wholesale (delete + reinsert) so
    /// keys dropped by warm-up/prune don't linger - the same whole-map semantics
    /// the old `state.json` rewrite had, but crash-safe via redb's commit.
    fn persist(&self) {
        self.persist_at(now_unix_secs());
    }

    /// As [`Self::persist`], recording `last` as the last check.
    fn persist_at(&self, last: i64) {
        let covers = self.read_covers().clone();
        let no_art = self
            .no_art
            .read()
            .unwrap_or_else(|e| e.into_inner())
            .clone();
        self.db.write(Durability::Immediate, |txn| {
            txn.delete_table(COVER_COVERS)?;
            {
                let mut table = txn.open_table(COVER_COVERS)?;
                for (key, hash) in &covers {
                    table.insert(key.as_str(), hash.as_str())?;
                }
            }
            txn.delete_table(COVER_NO_ART)?;
            {
                let mut table = txn.open_table(COVER_NO_ART)?;
                for (key, at) in &no_art {
                    table.insert(key.as_str(), *at)?;
                }
            }
            {
                let mut meta = txn.open_table(COVER_META)?;
                meta.insert(LAST_CHECK, last)?;
            }
            Ok(())
        });
    }

    /// Records an album with no artwork, in memory and at the next checkpoint.
    fn remember_no_art(&self, pending: &Mutex<Pending>, key: String) {
        let now = now_unix_secs();
        self.no_art
            .write()
            .unwrap_or_else(|e| e.into_inner())
            .insert(key.clone(), now);
        self.checkpoint(pending, |pending| pending.no_art.push((key, now)));
    }

    /// Queues one result with `add`, saving the batch once it is full.
    ///
    /// Appends only: the tables were rewritten at the start of the build, so
    /// everything in them is already valid.
    fn checkpoint(&self, pending: &Mutex<Pending>, add: impl FnOnce(&mut Pending)) {
        let batch = {
            let mut pending = pending.lock().unwrap_or_else(|e| e.into_inner());
            add(&mut pending);
            if pending.len() < CHECKPOINT_EVERY {
                return;
            }
            std::mem::take(&mut *pending)
        };
        self.db.write(Durability::Immediate, |txn| {
            let mut covers = txn.open_table(COVER_COVERS)?;
            for (key, hash) in &batch.covers {
                covers.insert(key.as_str(), hash.as_str())?;
            }
            let mut no_art = txn.open_table(COVER_NO_ART)?;
            for (key, at) in &batch.no_art {
                no_art.insert(key.as_str(), *at)?;
            }
            Ok(())
        });
    }

    /// The albums found to have no artwork, with when, as persisted.
    fn load_no_art(&self) -> HashMap<String, i64> {
        self.db
            .read(|txn| {
                let table = match txn.open_table(COVER_NO_ART) {
                    Ok(t) => t,
                    Err(redb::TableError::TableDoesNotExist(_)) => return Ok(HashMap::new()),
                    Err(e) => return Err(e.into()),
                };
                let mut map = HashMap::new();
                for entry in table.iter()? {
                    let (k, v) = entry?;
                    map.insert(k.value().to_string(), v.value());
                }
                Ok(map)
            })
            .unwrap_or_default()
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

/// One album handed from the producer to a worker: key, path, what to do, and
/// how long the producer took over it in milliseconds.
type Fetched = (String, String, Job, u128);

/// A worker's task for one album.
enum Job {
    /// Bytes the producer already has.
    Bytes(Vec<u8>),
    /// Artwork the worker reads itself.
    Read(Source),
}

/// Whether the core's own read of an album's artwork matches MusicBee's.
///
/// Sampled at the start of a build, and only when DEBUG is on: each sampled
/// album costs a second, MusicBee-side fetch.
struct Parity {
    left: usize,
    matched: usize,
    differed: usize,
    unreadable: usize,
}

impl Parity {
    fn new() -> Self {
        let left = if tracing::enabled!(tracing::Level::DEBUG) {
            PARITY_SAMPLE
        } else {
            0
        };
        Self {
            left,
            matched: 0,
            differed: 0,
            unreadable: 0,
        }
    }

    /// Turns a source into a job, reading it both ways first while sampling.
    fn check(&mut self, source: Source, path: &str, fetch: &dyn Fn(&str) -> Artwork) -> Job {
        if self.left == 0 {
            return Job::Read(source);
        }
        self.left -= 1;
        let ours = source.read();
        let theirs = fetch(path);
        match (&ours, &theirs) {
            (Ok(ours), Artwork::Found(theirs)) if ours == theirs => self.matched += 1,
            (Ok(ours), theirs) => {
                self.differed += 1;
                let theirs_len = match theirs {
                    Artwork::Found(raw) => raw.len(),
                    _ => 0,
                };
                tracing::debug!(
                    path,
                    ours = ours.len(),
                    theirs = theirs_len,
                    "artwork parity: different bytes"
                );
            }
            (Err(e), _) => {
                self.unreadable += 1;
                tracing::debug!(path, error = %e, "artwork parity: the core could not read it");
            }
        }
        match ours {
            Ok(raw) => Job::Bytes(raw),
            Err(_) => Job::Read(source),
        }
    }

    fn report(&self) {
        let sampled = self.matched + self.differed + self.unreadable;
        if sampled > 0 {
            tracing::info!(
                sampled,
                matched = self.matched,
                differed = self.differed,
                unreadable = self.unreadable,
                "artwork parity: the core's reads against MusicBee's"
            );
        }
    }
}

/// What the workers of one build share with each other and with the producer.
#[derive(Default)]
struct Shared {
    stored: AtomicUsize,
    failed: AtomicUsize,
    /// The largest source image stored so far.
    largest: Mutex<(u32, u32)>,
    /// Stored covers and no-art albums not yet saved by a checkpoint.
    pending: Mutex<Pending>,
    budget: Budget,
    read_in_core: AtomicUsize,
    /// Albums a worker could not read, for MusicBee to fetch after the pass.
    fallback: Mutex<Vec<(String, String)>>,
}

/// What the next checkpoint will save.
#[derive(Default)]
struct Pending {
    covers: Vec<(String, String)>,
    no_art: Vec<(String, i64)>,
}

impl Pending {
    fn len(&self) -> usize {
        self.covers.len() + self.no_art.len()
    }
}

/// The memory decodes may hold at once, shared by a build's workers.
///
/// A worker reserves its decode's estimated cost before starting and gives it
/// back when done; a decode larger than the whole budget waits for all of it,
/// so it runs alone rather than never.
struct Budget {
    free: Mutex<usize>,
    returned: Condvar,
}

impl Default for Budget {
    fn default() -> Self {
        Self {
            free: Mutex::new(DECODE_BUDGET),
            returned: Condvar::new(),
        }
    }
}

impl Budget {
    fn reserve(&self, cost: usize) -> Permit<'_> {
        let amount = cost.min(DECODE_BUDGET);
        let mut free = self.free.lock().unwrap_or_else(|e| e.into_inner());
        while *free < amount {
            free = self.returned.wait(free).unwrap_or_else(|e| e.into_inner());
        }
        *free -= amount;
        Permit {
            budget: self,
            amount,
        }
    }
}

/// A share of the [`Budget`], given back when dropped.
struct Permit<'a> {
    budget: &'a Budget,
    amount: usize,
}

impl Drop for Permit<'_> {
    fn drop(&mut self) {
        *self.budget.free.lock().unwrap_or_else(|e| e.into_inner()) += self.amount;
        self.budget.returned.notify_all();
    }
}

impl Shared {
    fn lock_fallback(&self) -> std::sync::MutexGuard<'_, Vec<(String, String)>> {
        self.fallback.lock().unwrap_or_else(|e| e.into_inner())
    }

    fn largest(&self) -> (u32, u32) {
        *self.largest.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// Keeps the larger of the recorded and `source` sizes, by pixel count.
    fn note_largest(&self, source: (u32, u32)) {
        let mut largest = self.largest.lock().unwrap_or_else(|e| e.into_inner());
        let area = |(w, h): (u32, u32)| u64::from(w) * u64::from(h);
        if area(source) > area(*largest) {
            *largest = source;
        }
    }
}

/// Logs where a build has got to, with the memory a 32-bit host runs out of.
fn report_progress(
    at: &BuildProgress<'_>,
    largest: (u32, u32),
    progress: &dyn Fn(&BuildProgress<'_>),
) {
    let (physical_mib, committed_mib) = crate::logging::memory_mib().unwrap_or_default();
    tracing::info!(
        done = at.done,
        total = at.total,
        stored = at.stored,
        failed = at.failed,
        physical_mib,
        committed_mib,
        largest = %format_args!("{}x{}", largest.0, largest.1),
        path = at.path,
        "cover cache build progress"
    );
    progress(at);
}

/// Lowers the calling thread below MusicBee's own, so a cold build does not
/// compete with playback or the UI for the CPU.
fn below_normal_priority() {
    #[cfg(windows)]
    {
        use windows_sys::Win32::System::Threading::{
            GetCurrentThread, SetThreadPriority, THREAD_PRIORITY_BELOW_NORMAL,
        };
        // SAFETY: GetCurrentThread returns a pseudo-handle valid for this call,
        // and SetThreadPriority only changes this thread's scheduling.
        unsafe {
            SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_BELOW_NORMAL);
        }
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
    use std::sync::atomic::AtomicUsize;

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
        // Otherwise a lost file leaves that album permanently blank.
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

    /// A cover is kept while its source file is unchanged, and changing the
    /// cache size does not change a source file - so without a recorded size the
    /// whole cache stays at whatever it was first built at.
    #[test]
    fn warm_up_drops_everything_when_the_cache_size_changed() {
        let (db, dir) = temp_storage("resized");
        let store = CoverStore::new(db.clone(), &dir);
        let album = AlbumIdentity {
            key: "alb1".into(),
            path: "/a.mp3".into(),
            modified: 0,
        };
        store.warm_up(std::slice::from_ref(&album));
        store.build(|_| Some(jpeg_bytes(400, 400)), false);
        assert!(store.hash_for("alb1").is_some());

        // A warm start at the same size keeps what is there.
        let same = CoverStore::new(db.clone(), &dir);
        same.warm_up(std::slice::from_ref(&album));
        assert!(
            same.hash_for("alb1").is_some(),
            "unchanged size keeps covers"
        );

        // Pretend the constant moved: the recorded size no longer agrees.
        db.write(Durability::Immediate, |txn| {
            let mut meta = txn.open_table(COVER_META)?;
            meta.insert(COVER_SIZE, i64::from(CACHE_SIZE) + 1)?;
            Ok(())
        });
        let resized = CoverStore::new(db.clone(), &dir);
        resized.warm_up(std::slice::from_ref(&album));
        assert_eq!(
            resized.hash_for("alb1"),
            None,
            "a new size rebuilds them all"
        );
    }

    fn albums(count: usize, modified: i64) -> Vec<AlbumIdentity> {
        (0..count)
            .map(|n| AlbumIdentity {
                key: format!("alb{n}"),
                path: format!("/{n}.mp3"),
                modified,
            })
            .collect()
    }

    /// Waits up to two seconds for the saved table to reach `at_least` covers.
    fn saved_reaches(store: &CoverStore, at_least: usize) -> bool {
        let deadline = Instant::now() + Duration::from_secs(2);
        while Instant::now() < deadline {
            if store.load_state().0.len() >= at_least {
                return true;
            }
            std::thread::sleep(Duration::from_millis(10));
        }
        false
    }

    /// The crash this guards against kills the process mid-build, so nothing
    /// after the build runs; what was checkpointed is all the next start gets.
    #[test]
    fn a_build_saves_covers_as_it_goes_not_only_at_the_end() {
        let (db, dir) = temp_storage("checkpoints");
        let store = CoverStore::new(db.clone(), &dir);
        let total = CHECKPOINT_EVERY * 2 + 50;
        store.warm_up(&albums(total, 0));

        let art = jpeg_bytes(8, 8);
        let fetched = AtomicUsize::new(0);
        let saved_mid_build = std::sync::atomic::AtomicBool::new(false);
        store.build(
            |_| {
                if fetched.fetch_add(1, Ordering::AcqRel) == total - 1 {
                    saved_mid_build
                        .store(saved_reaches(&store, CHECKPOINT_EVERY), Ordering::Release);
                }
                Some(art.clone())
            },
            false,
        );

        assert!(
            saved_mid_build.load(Ordering::Acquire),
            "a checkpoint was on disk before the build finished"
        );
        assert_eq!(
            store.load_state().0.len(),
            total,
            "and everything at the end"
        );
    }

    #[test]
    fn a_stale_cover_leaves_the_saved_table_before_the_build_writes_anything() {
        let (db, dir) = temp_storage("checkpoint-stale");
        let store = CoverStore::new(db.clone(), &dir);
        store.warm_up(&albums(2, 0));
        let art = jpeg_bytes(8, 8);
        store.build(|_| Some(art.clone()), false);

        // alb0's track changes, so warm-up drops its cover...
        let mut changed = albums(2, 0);
        changed[0].modified = now_unix_secs() + 60;
        store.warm_up(&changed);
        let table_at_first_fetch = Mutex::new(None);
        store.build(
            |_| {
                table_at_first_fetch
                    .lock()
                    .unwrap()
                    .get_or_insert_with(|| store.load_state().0);
                Some(art.clone())
            },
            false,
        );

        // ...and it is gone from disk too before any new cover is written, so a
        // later checkpoint cannot leave it looking valid.
        let table = table_at_first_fetch.lock().unwrap().take().unwrap();
        assert!(!table.contains_key("alb0"));
        assert!(table.contains_key("alb1"));
    }

    #[test]
    fn a_long_build_reports_its_progress() {
        let (db, dir) = temp_storage("progress");
        let store = CoverStore::new(db.clone(), &dir);
        let total = PROGRESS_EVERY + 10;
        store.warm_up(&albums(total, 0));
        let art = jpeg_bytes(8, 8);
        let reports = Mutex::new(Vec::new());
        let stats = store.build_reporting(|_| Some(art.clone()), false, &|| false, &|at| {
            reports
                .lock()
                .unwrap()
                .push((at.done, at.total, at.path.to_owned()))
        });

        let reports = reports.into_inner().unwrap();
        assert_eq!(reports.len(), 1);
        assert_eq!((reports[0].0, reports[0].1), (PROGRESS_EVERY, total));
        assert!(reports[0].2.ends_with(".mp3"));
        assert_eq!(stats.largest, (8, 8));
    }

    /// Builds `albums` with a fetch that counts its calls and answers `answer`.
    fn build_counting(store: &CoverStore, answer: fn() -> Artwork) -> usize {
        let calls = AtomicUsize::new(0);
        store.build(
            |_| {
                calls.fetch_add(1, Ordering::AcqRel);
                answer()
            },
            false,
        );
        calls.into_inner()
    }

    #[test]
    fn an_album_without_artwork_is_asked_once_and_remembered_across_restarts() {
        let (db, dir) = temp_storage("no-art");
        let store = CoverStore::new(db.clone(), &dir);
        store.warm_up(&albums(3, 0));
        assert_eq!(build_counting(&store, || Artwork::Missing), 3);
        store.warm_up(&albums(3, 0));
        assert_eq!(build_counting(&store, || Artwork::Missing), 0);

        let restarted = CoverStore::new(db.clone(), &dir);
        restarted.warm_up(&albums(3, 0));
        assert_eq!(build_counting(&restarted, || Artwork::Missing), 0);
    }

    #[test]
    fn a_failed_fetch_is_not_remembered_as_no_artwork() {
        let (db, dir) = temp_storage("no-art-failed");
        let store = CoverStore::new(db.clone(), &dir);
        store.warm_up(&albums(2, 0));
        let stats_calls = build_counting(&store, || Artwork::Unavailable);
        assert_eq!(stats_calls, 2);
        store.warm_up(&albums(2, 0));
        assert_eq!(build_counting(&store, || Artwork::Unavailable), 2);
    }

    #[test]
    fn no_artwork_is_asked_again_after_a_week_a_track_change_or_a_manual_rebuild() {
        let (db, dir) = temp_storage("no-art-retry");
        let store = CoverStore::new(db.clone(), &dir);
        store.warm_up(&albums(3, 0));
        build_counting(&store, || Artwork::Missing);

        // alb0 was found bare more than a week ago...
        db.write(Durability::Immediate, |txn| {
            txn.open_table(COVER_NO_ART)?
                .insert("alb0", now_unix_secs() - NO_ART_RETRY_SECS - 1)?;
            Ok(())
        });
        // ...and alb1's track has changed since.
        let mut changed = albums(3, 0);
        changed[1].modified = now_unix_secs() + 60;
        store.warm_up(&changed);
        assert_eq!(build_counting(&store, || Artwork::Missing), 2);

        store.forget_missing_art();
        assert_eq!(build_counting(&store, || Artwork::Missing), 3);
    }

    #[test]
    fn a_decode_bigger_than_the_whole_budget_still_runs_alone() {
        let budget = Budget::default();
        let half = budget.reserve(DECODE_BUDGET / 2);
        std::thread::scope(|s| {
            let waiting = s.spawn(|| {
                let _whole = budget.reserve(DECODE_BUDGET * 4);
                *budget.free.lock().unwrap()
            });
            std::thread::sleep(Duration::from_millis(50));
            assert!(!waiting.is_finished(), "it waits for the half still out");
            drop(half);
            assert_eq!(waiting.join().unwrap(), 0, "and then holds all of it");
        });
        assert_eq!(
            *budget.free.lock().unwrap(),
            DECODE_BUDGET,
            "all given back"
        );
    }

    /// Builds with `locate` and a MusicBee-side fetch that counts its calls.
    fn build_with_lookup(
        store: &CoverStore,
        locate: impl Fn(&str) -> Lookup,
        fetch_answer: fn() -> Artwork,
    ) -> (BuildStats, usize) {
        let fetched = AtomicUsize::new(0);
        let stats = store.build_located(
            locate,
            |_| {
                fetched.fetch_add(1, Ordering::AcqRel);
                fetch_answer()
            },
            false,
            &|| false,
            &|_| {},
        );
        (stats, fetched.into_inner())
    }

    #[test]
    fn linked_artwork_is_read_by_the_workers_without_asking_musicbee() {
        let (db, dir) = temp_storage("read-in-core");
        let image = dir.join("cover.jpg");
        std::fs::write(&image, jpeg_bytes(300, 300)).unwrap();
        let store = CoverStore::new(db.clone(), &dir);
        store.warm_up(&albums(3, 0));
        let (stats, fetched) = build_with_lookup(
            &store,
            |_| Lookup::Read(Source::File(image.clone())),
            || Artwork::Unavailable,
        );
        assert_eq!(
            (stats.stored, stats.read_in_core, stats.fell_back),
            (3, 3, 0)
        );
        assert_eq!(fetched, 0);
        assert_eq!(store.cached_count(), 3);
    }

    #[test]
    fn what_the_core_cannot_read_is_fetched_through_musicbee_once() {
        let (db, dir) = temp_storage("read-fallback");
        let store = CoverStore::new(db.clone(), &dir);
        store.warm_up(&albums(2, 0));
        let missing = dir.join("gone.jpg");
        let (stats, fetched) = build_with_lookup(
            &store,
            |_| Lookup::Read(Source::File(missing.clone())),
            || Artwork::Found(jpeg_bytes(300, 300)),
        );
        assert_eq!(
            (stats.stored, stats.read_in_core, stats.fell_back),
            (2, 0, 2)
        );
        assert_eq!(fetched, 2);
        assert_eq!(store.cached_count(), 2);
    }

    #[test]
    fn a_fallback_that_finds_no_artwork_is_remembered() {
        let (db, dir) = temp_storage("read-fallback-bare");
        let store = CoverStore::new(db.clone(), &dir);
        store.warm_up(&albums(1, 0));
        let missing = dir.join("gone.jpg");
        let (stats, _) = build_with_lookup(
            &store,
            |_| Lookup::Read(Source::File(missing.clone())),
            || Artwork::Missing,
        );
        assert_eq!(stats.no_art, 1);
        store.warm_up(&albums(1, 0));
        assert_eq!(build_counting(&store, || Artwork::Missing), 0);
    }

    #[test]
    fn the_parity_check_counts_matches_differences_and_unreadable_files() {
        let (_, dir) = temp_storage("parity");
        let image = dir.join("cover.jpg");
        std::fs::write(&image, [1u8, 2, 3]).unwrap();
        let mut parity = Parity {
            left: 3,
            matched: 0,
            differed: 0,
            unreadable: 0,
        };
        let same = |_: &str| Artwork::Found(vec![1, 2, 3]);
        let other = |_: &str| Artwork::Found(vec![9]);
        assert!(matches!(
            parity.check(Source::File(image.clone()), "a", &same),
            Job::Bytes(_)
        ));
        parity.check(Source::File(image.clone()), "b", &other);
        let gone = parity.check(Source::File(dir.join("gone.jpg")), "c", &same);
        assert!(
            matches!(gone, Job::Read(_)),
            "an unreadable one still goes to a worker, to fall back"
        );
        assert_eq!(
            (parity.matched, parity.differed, parity.unreadable),
            (1, 1, 1)
        );
        assert!(
            matches!(parity.check(Source::File(image), "d", &same), Job::Read(_)),
            "sampling is over"
        );
    }

    #[test]
    fn build_stops_when_asked_and_keeps_what_it_built() {
        // Teardown waits for this build, and the next one recomputes what is
        // missing rather than starting over.
        let (db, dir) = temp_storage("build-stops");
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
            AlbumIdentity {
                key: "alb3".into(),
                path: "/c.mp3".into(),
                modified: 0,
            },
        ]);

        let art = jpeg_bytes(300, 300);
        let fetched = AtomicUsize::new(0);
        let stats = store.build_until(
            |_| {
                fetched.fetch_add(1, Ordering::AcqRel);
                Some(art.clone())
            },
            false,
            // Stop once the first album has been fetched.
            &|| fetched.load(Ordering::Acquire) >= 1,
        );

        assert!(
            stats.stopped,
            "the build should report that it gave up early"
        );
        assert_eq!(fetched.load(Ordering::Acquire), 1);
        assert_eq!(stats.stored, 1);
        assert_eq!(store.cached_count(), 1, "the one it built is kept");
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

    #[test]
    fn deleting_one_album_keeps_a_cover_another_album_still_uses() {
        // Identical artwork means one content-hashed file for both albums, so
        // this is the deletion safety the nudge-path cover delta relies on.
        let (db, dir) = temp_storage("shared-delete");
        let store = CoverStore::new(db.clone(), &dir);
        let art = jpeg_bytes(200, 200);

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
        store.build(|_| Some(art.clone()), false);

        // Same artwork -> same hash -> one shared file referenced by both albums.
        let hash = store.hash_for("alb1").expect("alb1 cached");
        assert_eq!(store.hash_for("alb2"), Some(hash.clone()), "shared hash");
        assert!(store.cover_file(&hash).exists());

        // Delete alb2 (gone from the album list). alb1 still holds the hash, so a
        // re-warm + build keeps the shared file.
        let store = CoverStore::new(db.clone(), &dir);
        store.warm_up(&[AlbumIdentity {
            key: "alb1".into(),
            path: "/a.mp3".into(),
            modified: 0,
        }]);
        store.build(|_| Some(art.clone()), false);
        assert_eq!(store.hash_for("alb2"), None, "deleted album key dropped");
        assert!(
            store.cover_file(&hash).exists(),
            "shared cover file must survive while another album uses it"
        );

        // Delete alb1 too: nothing references the hash now, so it is pruned.
        let store = CoverStore::new(db, &dir);
        store.warm_up(&[]);
        store.build(|_| Some(art.clone()), false);
        assert!(
            !store.cover_file(&hash).exists(),
            "cover file pruned once no album references it"
        );
    }
}
