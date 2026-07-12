//! The shared embedded key-value store (`redb`) backing the core's durable
//! caches: the album-cover index (formerly `state.json`) and the library
//! metadata cache. One `<storage>/mbrc.redb` file holds several typed tables,
//! opened once and shared (via `Arc`) by both `CoverStore` and `MetadataCache`.
//!
//! [`Db`] wraps the database in `Option<Arc<..>>` so an absent or unopenable
//! store degrades to a no-op - the caches are always rebuildable, so
//! persistence is best-effort and never fatal, exactly like `Config::load`.
//! Unit/integration tests build a `Config` with an empty storage path, which
//! yields `Db(None)` and disables persistence.

use std::collections::HashMap;
use std::path::Path;
use std::sync::Arc;

use redb::{
    Builder, Database, DatabaseError, Durability, ReadTransaction, ReadableDatabase,
    TableDefinition, WriteTransaction,
};

/// redb page-cache ceiling. The core is a 32-bit (i686) process, so we pin a
/// small deterministic cap rather than let redb's default grow opportunistically
/// into the ~2 GB address space. redb 4.x reads pages off disk through this
/// cache (no mmap), and our access is O(page), so the working set stays far
/// under this - it is a ceiling, not a reservation. See MBRCIP-0001 §9.
const CACHE_SIZE: usize = 64 * 1024 * 1024;

/// album_key -> content_hash (the resized cover's SHA1). Replaces the `covers`
/// map in the old `state.json`.
pub const COVER_COVERS: TableDefinition<&str, &str> = TableDefinition::new("cover_covers");
/// Cover-cache scalars keyed by name. `"last_check"` -> unix seconds (the former
/// `state.json` `paths` field - a legacy naming quirk, dropped now that the value
/// is its own row).
pub const COVER_META: TableDefinition<&str, i64> = TableDefinition::new("cover_meta");
/// Core-wide scalars: migration markers and the library fingerprint. Values are
/// opaque bytes (a 1-byte marker, a u64 LE, ...).
pub const META: TableDefinition<&str, &[u8]> = TableDefinition::new("meta");
/// Library metadata cache: a canonical query key -> the rmp-serialized response.
pub const METADATA_CACHE: TableDefinition<&str, &[u8]> = TableDefinition::new("metadata_cache");
/// Ordinal track path index (MBRCIP-0001): `u32` position (browse order) -> path.
/// A redb range over this key IS the O(page) browse pagination - no full-library
/// list ever materializes. Rewritten wholesale by the Scanner on add/delete/
/// reorder; the tag cache below survives because it is keyed by path, not order.
pub const TRACK_PATHS: TableDefinition<u32, &str> = TableDefinition::new("track_paths");
/// Path-keyed track tag cache (MBRCIP-0001): path -> rmp-serialized `Track` (the
/// 7 browse fields). Keyed by the raw path (exact, order-independent) rather than
/// a hash - redb takes variable-length keys, so hashing buys only a smaller key
/// and a collision risk we don't need. A page's misses are filled lazily via one
/// FFI batch and written here.
pub const TRACK_TAGS: TableDefinition<&str, &[u8]> = TableDefinition::new("track_tags");

/// [`COVER_META`] key holding the last cache-check time (unix seconds).
pub const LAST_CHECK: &str = "last_check";
/// [`META`] key holding the last library fingerprint (u64 LE).
pub const LIBRARY_FINGERPRINT: &str = "library_fingerprint";
/// [`META`] key holding the tracks-cache sync watermark (unix seconds, i64 LE) -
/// the `updated_since` the Scanner passes to `LibrarySyncDelta` each pass.
pub const TRACKS_SYNCED_AT: &str = "tracks_synced_at";
/// [`META`] marker key recording that the one-time `state.json` import ran.
const COVER_STATE_MIGRATED: &str = "cover_state_migrated";

/// The legacy on-disk cover state (`cache/state.json`), read once to import into
/// redb. `paths` is the old `LastCheck` field (a `[DataMember(Name="paths")]`
/// quirk from the shipped C# plugin).
#[derive(serde::Deserialize)]
struct LegacyState {
    #[serde(default)]
    covers: HashMap<String, String>,
    #[serde(default, rename = "paths")]
    last_check: i64,
}

/// A shared handle to the core's redb database. Clones share the same file via
/// `Arc`. `None` means persistence is disabled (no storage path, or the file
/// could not be opened) and every operation is a best-effort no-op.
#[derive(Clone)]
pub struct Db(Option<Arc<Database>>);

impl Db {
    /// Open (or create) `<storage_path>/mbrc.redb`. An empty path disables
    /// persistence. A corrupt file is deleted and recreated once; a still-failing
    /// open also degrades to `Db(None)` (logged, never fatal).
    pub fn open(storage_path: &str) -> Self {
        if storage_path.is_empty() {
            return Db(None);
        }
        let path = Path::new(storage_path).join("mbrc.redb");
        match Self::create_db(&path) {
            Ok(db) => Db(Some(Arc::new(db))),
            Err(e) => {
                tracing::warn!(error = %e, "mbrc.redb open failed; recreating fresh");
                let _ = std::fs::remove_file(&path);
                match Self::create_db(&path) {
                    Ok(db) => Db(Some(Arc::new(db))),
                    Err(e) => {
                        tracing::error!(error = %e, "mbrc.redb unusable; persistence disabled");
                        Db(None)
                    }
                }
            }
        }
    }

    /// Open/create the database with the [`CACHE_SIZE`] page-cache cap applied.
    fn create_db(path: &Path) -> Result<Database, DatabaseError> {
        Builder::new().set_cache_size(CACHE_SIZE).create(path)
    }

    /// A disabled store (persistence off). For tests that need a `CoverStore`
    /// without touching disk beyond the covers dir.
    pub fn disabled() -> Self {
        Db(None)
    }

    /// Whether persistence is active (a database is open).
    pub fn is_active(&self) -> bool {
        self.0.is_some()
    }

    /// Run a read transaction, returning the closure's value, or `None` when
    /// persistence is off or any redb step fails (treated as a cache miss).
    pub fn read<T>(&self, f: impl FnOnce(&ReadTransaction) -> Result<T, redb::Error>) -> Option<T> {
        let db = self.0.as_deref()?;
        match db.begin_read() {
            Ok(txn) => match f(&txn) {
                Ok(value) => Some(value),
                Err(e) => {
                    tracing::debug!(error = %e, "redb read failed");
                    None
                }
            },
            Err(e) => {
                tracing::debug!(error = %e, "redb begin_read failed");
                None
            }
        }
    }

    /// Run a write transaction at the given durability and commit it. A no-op
    /// when persistence is off; any failure is logged and swallowed (the cache
    /// stays rebuildable). redb 4.x offers only `None`/`Immediate` durability;
    /// these caches must survive a restart, so callers pass `Immediate`.
    pub fn write(
        &self,
        durability: Durability,
        f: impl FnOnce(&WriteTransaction) -> Result<(), redb::Error>,
    ) {
        let Some(db) = self.0.as_deref() else {
            return;
        };
        let mut txn = match db.begin_write() {
            Ok(txn) => txn,
            Err(e) => {
                tracing::warn!(error = %e, "redb begin_write failed");
                return;
            }
        };
        // Only fails if a persistent savepoint was modified in this txn (never
        // here), so the result is safe to ignore.
        let _ = txn.set_durability(durability);
        let result = f(&txn).and_then(|()| txn.commit().map_err(redb::Error::from));
        if let Err(e) = result {
            tracing::warn!(error = %e, "redb write failed");
        }
    }

    /// One-time import of the shipped plugin's `cache/state.json` into redb, so
    /// an existing built cover cache survives the upgrade (no from-scratch
    /// rebuild). Guarded by a marker in [`META`] - runs at most once ever, even
    /// if the covers dir is later cleared. Best-effort; any failure logs and
    /// leaves the cache to rebuild.
    pub fn migrate_cover_state(&self, storage_path: &str) {
        if !self.is_active() || self.has_marker(COVER_STATE_MIGRATED) {
            return;
        }
        let state_path = Path::new(storage_path).join("cache").join("state.json");
        let Ok(text) = std::fs::read_to_string(&state_path) else {
            // Nothing to migrate (fresh install / already renamed). Mark done so
            // we don't stat the file every launch.
            self.set_marker(COVER_STATE_MIGRATED);
            return;
        };
        let Ok(state) = serde_json::from_str::<LegacyState>(&text) else {
            tracing::warn!("legacy state.json unparseable; skipping cover-state migration");
            self.set_marker(COVER_STATE_MIGRATED);
            return;
        };

        let count = state.covers.len();
        self.write(Durability::Immediate, |txn| {
            {
                let mut covers = txn.open_table(COVER_COVERS)?;
                for (key, hash) in &state.covers {
                    covers.insert(key.as_str(), hash.as_str())?;
                }
            }
            {
                let mut meta = txn.open_table(COVER_META)?;
                meta.insert(LAST_CHECK, state.last_check)?;
            }
            {
                let mut m = txn.open_table(META)?;
                m.insert(COVER_STATE_MIGRATED, [1u8].as_slice())?;
            }
            Ok(())
        });
        // Rename so a manual redb delete doesn't re-import stale rows on top of a
        // fresh library (the marker also guards this, belt and braces).
        let _ = std::fs::rename(&state_path, state_path.with_extension("json.migrated"));
        tracing::info!(covers = count, "migrated cover state.json -> mbrc.redb");
    }

    /// Whether a 1-byte marker key is present in [`META`].
    fn has_marker(&self, key: &str) -> bool {
        self.read(|txn| {
            let table = match txn.open_table(META) {
                Ok(t) => t,
                Err(redb::TableError::TableDoesNotExist(_)) => return Ok(false),
                Err(e) => return Err(e.into()),
            };
            Ok(table.get(key)?.is_some())
        })
        .unwrap_or(false)
    }

    /// Set a 1-byte marker key in [`META`].
    fn set_marker(&self, key: &str) {
        self.write(Durability::Immediate, |txn| {
            let mut m = txn.open_table(META)?;
            m.insert(key, [1u8].as_slice())?;
            Ok(())
        });
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_dir(name: &str) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("mbrc-store-{name}"));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    #[test]
    fn disabled_db_is_a_noop() {
        let db = Db::disabled();
        assert!(!db.is_active());
        // Reads miss, writes are swallowed, no panic.
        assert!(db.read(|_txn| Ok(())).is_none());
        db.write(Durability::Immediate, |_txn| Ok(()));
        db.migrate_cover_state("");
    }

    #[test]
    fn open_creates_file_and_roundtrips_a_table() {
        let dir = temp_dir("open");
        let db = Db::open(dir.to_str().unwrap());
        assert!(db.is_active());
        assert!(dir.join("mbrc.redb").exists());

        db.write(Durability::Immediate, |txn| {
            let mut t = txn.open_table(COVER_COVERS)?;
            t.insert("k", "hash")?;
            Ok(())
        });
        let got = db.read(|txn| {
            let t = txn.open_table(COVER_COVERS)?;
            Ok(t.get("k")?.map(|g| g.value().to_string()))
        });
        assert_eq!(got, Some(Some("hash".to_string())));
    }

    #[test]
    fn migrate_imports_state_json_once() {
        let dir = temp_dir("migrate");
        let cache = dir.join("cache");
        std::fs::create_dir_all(&cache).unwrap();
        std::fs::write(
            cache.join("state.json"),
            r#"{"covers":{"alb1":"deadbeef"},"paths":1234}"#,
        )
        .unwrap();

        let db = Db::open(dir.to_str().unwrap());
        db.migrate_cover_state(dir.to_str().unwrap());

        // Covers + last_check imported.
        let hash = db.read(|txn| {
            let t = txn.open_table(COVER_COVERS)?;
            Ok(t.get("alb1")?.map(|g| g.value().to_string()))
        });
        assert_eq!(hash, Some(Some("deadbeef".to_string())));
        let last = db.read(|txn| {
            let t = txn.open_table(COVER_META)?;
            Ok(t.get(LAST_CHECK)?.map(|g| g.value()))
        });
        assert_eq!(last, Some(Some(1234)));

        // state.json renamed, marker set - a second run is a no-op even if the
        // file reappears.
        assert!(!cache.join("state.json").exists());
        assert!(cache.join("state.json.migrated").exists());
        std::fs::write(cache.join("state.json"), r#"{"covers":{"alb2":"beef"}}"#).unwrap();
        db.migrate_cover_state(dir.to_str().unwrap());
        let alb2 = db.read(|txn| {
            let t = txn.open_table(COVER_COVERS)?;
            Ok(t.get("alb2")?.map(|g| g.value().to_string()))
        });
        assert_eq!(alb2, Some(None), "second migration must not re-import");
    }
}
