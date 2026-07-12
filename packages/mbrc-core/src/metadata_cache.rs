//! The library metadata cache: a redb-backed store for browse/navigation query
//! responses, so the core stops re-crossing the FFI (and making MusicBee
//! re-scan the whole library) for data it already holds.
//!
//! Population (see the plan):
//! - Flat browse lists (genres/artists/albums/tracks) are EAGER: the full list
//!   is fetched once (at init / after a library change) and cached whole; pages
//!   are served by slicing locally.
//! - Hierarchical navs (genre_artists/artist_albums/album_tracks) are LAZY:
//!   cached per name key on first request.
//!
//! A `validated` gate guards correctness at startup and around a library switch:
//! until the fingerprint is reconciled, reads and writes are no-ops so handlers
//! serve straight through to the provider (correct, just uncached). Persistence
//! is best-effort (see [`crate::store::Db`]); a disabled `Db` makes every method
//! a no-op and the cache transparently falls back to the provider.

use std::sync::atomic::{AtomicBool, Ordering};

use redb::{Durability, ReadableTableMetadata};
use serde::de::DeserializeOwned;
use serde::Serialize;

use crate::protocol::messages::Track;
use crate::store::{
    Db, LIBRARY_FINGERPRINT, META, METADATA_CACHE, TRACKS_SYNCED_AT, TRACK_PATHS, TRACK_TAGS,
};

pub struct MetadataCache {
    db: Db,
    validated: AtomicBool,
}

impl MetadataCache {
    pub fn new(db: Db) -> Self {
        Self {
            db,
            validated: AtomicBool::new(false),
        }
    }

    /// Whether the cache has been reconciled against the current library and is
    /// live for reads/writes.
    pub fn is_validated(&self) -> bool {
        self.validated.load(Ordering::Acquire)
    }

    fn set_validated(&self, value: bool) {
        self.validated.store(value, Ordering::Release);
    }

    /// Read a cached response by key, deserialized to `T`. `None` when the cache
    /// is disabled or not yet validated, on a miss, or on a decode error - the
    /// caller then falls back to the provider.
    pub fn get<T: DeserializeOwned>(&self, key: &str) -> Option<T> {
        if !self.is_validated() {
            return None;
        }
        let bytes = self.db.read(|txn| {
            let table = match txn.open_table(METADATA_CACHE) {
                Ok(t) => t,
                Err(redb::TableError::TableDoesNotExist(_)) => return Ok(None),
                Err(e) => return Err(e.into()),
            };
            Ok(table.get(key)?.map(|g| g.value().to_vec()))
        })??;
        rmp_serde::from_slice(&bytes).ok()
    }

    /// Whether `key` is present, without deserializing its value. `false` when
    /// the cache is disabled, not yet validated, or the key is absent. Used to
    /// decide whether an eager list still needs building without paying to
    /// decode a (potentially large) cached list.
    pub fn contains(&self, key: &str) -> bool {
        if !self.is_validated() {
            return false;
        }
        self.db
            .read(|txn| {
                let table = match txn.open_table(METADATA_CACHE) {
                    Ok(t) => t,
                    Err(redb::TableError::TableDoesNotExist(_)) => return Ok(false),
                    Err(e) => return Err(e.into()),
                };
                Ok(table.get(key)?.is_some())
            })
            .unwrap_or(false)
    }

    /// Cache a response under `key`. No-op when disabled or not validated.
    pub fn put<T: Serialize>(&self, key: &str, value: &T) {
        if !self.is_validated() {
            return;
        }
        let Ok(bytes) = rmp_serde::to_vec_named(value) else {
            return;
        };
        self.db.write(Durability::Immediate, |txn| {
            let mut table = txn.open_table(METADATA_CACHE)?;
            table.insert(key, bytes.as_slice())?;
            Ok(())
        });
    }

    /// Drop every cached entry (used on a library change): the generic blob cache
    /// plus the track ordinal index and path-keyed tag cache. Resets the tracks
    /// watermark so the next scan rebuilds from scratch, but keeps the `META`
    /// table (it also holds the library fingerprint). Does not touch the
    /// `validated` flag.
    pub fn clear(&self) {
        self.db.write(Durability::Immediate, |txn| {
            // `delete_table` returns false if it never existed - harmless.
            txn.delete_table(METADATA_CACHE)?;
            txn.delete_table(TRACK_PATHS)?;
            txn.delete_table(TRACK_TAGS)?;
            let mut meta = txn.open_table(META)?;
            meta.remove(TRACKS_SYNCED_AT)?;
            Ok(())
        });
    }

    /// Runtime library switch: gate reads off and clear the table. The follow-up
    /// [`reconcile`](Self::reconcile) re-validates once the new library is
    /// fingerprinted.
    pub fn invalidate(&self) {
        self.set_validated(false);
        self.clear();
    }

    // ── Track ordinal index + path-keyed tag cache (MBRCIP-0001) ──
    //
    // The tracks list is the only browse list large enough to OOM the 32-bit
    // core as a single blob, so it is stored as an ordinal path index
    // (`TRACK_PATHS`, browse order) plus a path-keyed tag cache (`TRACK_TAGS`).
    // A browse page reads one redb range over the index and looks up each path's
    // tags - O(page), never the whole library. All reads/writes honor the same
    // `validated` gate as the generic cache above.

    /// Number of tracks in the ordinal index. Zero when disabled, not validated,
    /// or the index is empty.
    pub fn track_count(&self) -> u64 {
        if !self.is_validated() {
            return 0;
        }
        self.db
            .read(|txn| {
                let table = match txn.open_table(TRACK_PATHS) {
                    Ok(t) => t,
                    Err(redb::TableError::TableDoesNotExist(_)) => return Ok(0),
                    Err(e) => return Err(e.into()),
                };
                Ok(table.len()?)
            })
            .unwrap_or(0)
    }

    /// The track paths for the browse page `[offset, offset+limit)`, read straight
    /// from the ordinal index via a redb range - O(page), never the whole library.
    /// `limit <= 0` means "the rest from offset" (matches C# `Paginate`). Empty
    /// when disabled/unvalidated so the caller falls back to the provider.
    pub fn track_page_paths(&self, offset: i32, limit: i32) -> Vec<String> {
        if !self.is_validated() {
            return Vec::new();
        }
        let start = offset.max(0) as u32;
        let end = if limit > 0 {
            start.saturating_add(limit as u32)
        } else {
            u32::MAX
        };
        self.db
            .read(|txn| {
                let table = match txn.open_table(TRACK_PATHS) {
                    Ok(t) => t,
                    Err(redb::TableError::TableDoesNotExist(_)) => return Ok(Vec::new()),
                    Err(e) => return Err(e.into()),
                };
                let mut paths = Vec::new();
                for row in table.range(start..end)? {
                    let (_pos, path) = row?;
                    paths.push(path.value().to_string());
                }
                Ok(paths)
            })
            .unwrap_or_default()
    }

    /// Replace the ordinal index with `paths` in browse order (positions `0..n`).
    /// Drops the previous index first, so add / delete / reorder all converge.
    /// No-op when disabled or not validated.
    pub fn replace_track_index(&self, paths: &[String]) {
        if !self.is_validated() {
            return;
        }
        self.db.write(Durability::Immediate, |txn| {
            // Drop the whole table (cheaper than per-row deletes), then rebuild
            // positions from scratch.
            txn.delete_table(TRACK_PATHS)?;
            let mut table = txn.open_table(TRACK_PATHS)?;
            for (i, path) in paths.iter().enumerate() {
                table.insert(i as u32, path.as_str())?;
            }
            Ok(())
        });
    }

    /// The cached `Track` for a path, if present. `None` when disabled,
    /// unvalidated, on a miss, or a decode error - the caller then fills it via
    /// one FFI batch and writes it back with [`put_track_tags`](Self::put_track_tags).
    pub fn track_tags(&self, path: &str) -> Option<Track> {
        if !self.is_validated() {
            return None;
        }
        let bytes = self.db.read(|txn| {
            let table = match txn.open_table(TRACK_TAGS) {
                Ok(t) => t,
                Err(redb::TableError::TableDoesNotExist(_)) => return Ok(None),
                Err(e) => return Err(e.into()),
            };
            Ok(table.get(path)?.map(|g| g.value().to_vec()))
        })??;
        rmp_serde::from_slice(&bytes).ok()
    }

    /// Cache the given tracks, keyed by each track's `src` path, in one write
    /// transaction. No-op when disabled, not validated, or empty.
    pub fn put_track_tags(&self, tracks: &[Track]) {
        if !self.is_validated() || tracks.is_empty() {
            return;
        }
        self.db.write(Durability::Immediate, |txn| {
            let mut table = txn.open_table(TRACK_TAGS)?;
            for track in tracks {
                if let Ok(bytes) = rmp_serde::to_vec_named(track) {
                    table.insert(track.src.as_str(), bytes.as_slice())?;
                }
            }
            Ok(())
        });
    }

    /// Drop cached tags for these paths (a delta marked them changed; they are
    /// re-read lazily on the next serve). No-op when disabled, not validated, or
    /// empty.
    pub fn drop_track_tags(&self, paths: &[String]) {
        if !self.is_validated() || paths.is_empty() {
            return;
        }
        self.db.write(Durability::Immediate, |txn| {
            let mut table = txn.open_table(TRACK_TAGS)?;
            for path in paths {
                table.remove(path.as_str())?;
            }
            Ok(())
        });
    }

    /// The tracks-cache sync watermark (unix seconds). Zero when never synced.
    pub fn tracks_synced_at(&self) -> i64 {
        self.db
            .read(|txn| {
                let table = match txn.open_table(META) {
                    Ok(t) => t,
                    Err(redb::TableError::TableDoesNotExist(_)) => return Ok(0),
                    Err(e) => return Err(e.into()),
                };
                Ok(table
                    .get(TRACKS_SYNCED_AT)?
                    .and_then(|g| <[u8; 8]>::try_from(g.value()).ok().map(i64::from_le_bytes))
                    .unwrap_or(0))
            })
            .unwrap_or(0)
    }

    /// Record the tracks-cache sync watermark (unix seconds).
    pub fn set_tracks_synced_at(&self, ts: i64) {
        self.db.write(Durability::Immediate, |txn| {
            let mut table = txn.open_table(META)?;
            table.insert(TRACKS_SYNCED_AT, ts.to_le_bytes().as_slice())?;
            Ok(())
        });
    }

    /// Reconcile the stored library fingerprint against the current one. When
    /// they differ (or none is stored), the cache is stale for this library, so
    /// it is cleared and the new fingerprint recorded. Either way the cache is
    /// marked validated, so reads/writes go live afterward. Returns whether the
    /// cache was cleared (i.e. the library changed).
    pub fn reconcile(&self, fingerprint: u64) -> bool {
        let changed = self.stored_fingerprint() != Some(fingerprint);
        if changed {
            self.clear();
            self.store_fingerprint(fingerprint);
        }
        self.set_validated(true);
        changed
    }

    fn stored_fingerprint(&self) -> Option<u64> {
        self.db.read(|txn| {
            let table = match txn.open_table(META) {
                Ok(t) => t,
                Err(redb::TableError::TableDoesNotExist(_)) => return Ok(None),
                Err(e) => return Err(e.into()),
            };
            Ok(table
                .get(LIBRARY_FINGERPRINT)?
                .and_then(|g| <[u8; 8]>::try_from(g.value()).ok().map(u64::from_le_bytes)))
        })?
    }

    fn store_fingerprint(&self, fp: u64) {
        self.db.write(Durability::Immediate, |txn| {
            let mut table = txn.open_table(META)?;
            table.insert(LIBRARY_FINGERPRINT, fp.to_le_bytes().as_slice())?;
            Ok(())
        });
    }
}

/// A deterministic, order-independent fingerprint of the library's album
/// identities (their keys + modification times), used to detect a library
/// change across restarts and switches.
///
/// MUST stay deterministic across processes: inline FNV-1a, never
/// `DefaultHasher`/`RandomState` (their SipHash key is randomized per run, so a
/// stored fingerprint would never match on the next launch).
pub fn fingerprint<'a>(albums: impl IntoIterator<Item = (&'a str, i64)>) -> u64 {
    let mut acc: u64 = 0;
    let mut count: u64 = 0;
    for (key, modified) in albums {
        // XOR the key hash with the mod time, sum with wrapping add so album
        // order does not matter.
        acc = acc.wrapping_add(fnv1a_64(key.as_bytes()) ^ (modified as u64));
        count = count.wrapping_add(1);
    }
    // Mix in the count so adding + removing albums whose terms happen to cancel
    // still changes the fingerprint.
    acc ^ count.wrapping_mul(0x9E37_79B9_7F4A_7C15)
}

fn fnv1a_64(bytes: &[u8]) -> u64 {
    const OFFSET: u64 = 0xcbf2_9ce4_8422_2325;
    const PRIME: u64 = 0x0000_0100_0000_01b3;
    let mut hash = OFFSET;
    for &b in bytes {
        hash ^= b as u64;
        hash = hash.wrapping_mul(PRIME);
    }
    hash
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_db(name: &str) -> Db {
        let dir = std::env::temp_dir().join(format!("mbrc-meta-{name}"));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        Db::open(dir.to_str().unwrap())
    }

    #[test]
    fn no_ops_until_validated_then_round_trips() {
        let cache = MetadataCache::new(temp_db("gate"));
        // Not validated: put is a no-op, get misses.
        cache.put("k", &vec![1u32, 2, 3]);
        assert_eq!(cache.get::<Vec<u32>>("k"), None);

        // First reconcile validates (nothing stored -> "changed").
        assert!(cache.reconcile(42));
        cache.put("k", &vec![1u32, 2, 3]);
        assert_eq!(cache.get::<Vec<u32>>("k"), Some(vec![1, 2, 3]));

        // Invalidate gates reads off and clears.
        cache.invalidate();
        assert!(!cache.is_validated());
        assert_eq!(cache.get::<Vec<u32>>("k"), None);
    }

    #[test]
    fn contains_tracks_presence_without_decode() {
        let cache = MetadataCache::new(temp_db("contains"));
        // Not validated yet: everything reads absent.
        assert!(!cache.contains("k"));
        cache.reconcile(1);
        assert!(!cache.contains("k"), "absent key");
        cache.put("k", &vec![1u32, 2, 3]);
        assert!(cache.contains("k"), "present after put");
        // A library change clears the table, so presence drops.
        cache.reconcile(2);
        assert!(!cache.contains("k"), "cleared on fingerprint change");
    }

    #[test]
    fn reconcile_clears_only_on_fingerprint_change() {
        let cache = MetadataCache::new(temp_db("recon"));
        assert!(cache.reconcile(1), "first fingerprint counts as a change");
        cache.put("k", &7u32);
        assert!(!cache.reconcile(1), "same fingerprint: cache kept");
        assert_eq!(cache.get::<u32>("k"), Some(7));
        assert!(cache.reconcile(2), "new fingerprint: change");
        assert_eq!(cache.get::<u32>("k"), None, "cache cleared on change");
    }

    #[test]
    fn fingerprint_deterministic_order_independent_and_sensitive() {
        let a = [("alb1", 100i64), ("alb2", 200)];
        let shuffled = [("alb2", 200i64), ("alb1", 100)];
        assert_eq!(
            fingerprint(a.iter().copied()),
            fingerprint(shuffled.iter().copied()),
            "order must not matter"
        );
        let retagged = [("alb1", 101i64), ("alb2", 200)];
        assert_ne!(
            fingerprint(a.iter().copied()),
            fingerprint(retagged.iter().copied()),
            "a changed mod-time must change the fingerprint"
        );
        let removed = [("alb1", 100i64)];
        assert_ne!(
            fingerprint(a.iter().copied()),
            fingerprint(removed.iter().copied()),
            "add/remove must change the fingerprint"
        );
    }

    #[test]
    fn track_index_pages_and_tag_cache_round_trip() {
        let cache = MetadataCache::new(temp_db("tracks"));
        // Gated until validated.
        cache.replace_track_index(&["a".into(), "b".into()]);
        assert_eq!(cache.track_count(), 0, "no-op until validated");
        assert!(cache.track_page_paths(0, 10).is_empty());

        cache.reconcile(1);
        let paths: Vec<String> = (0..5).map(|i| format!("/m/{i}.mp3")).collect();
        cache.replace_track_index(&paths);
        assert_eq!(cache.track_count(), 5);

        // Range paging: offset 1, limit 2 -> items 1,2, in index order.
        assert_eq!(
            cache.track_page_paths(1, 2),
            vec!["/m/1.mp3".to_string(), "/m/2.mp3".to_string()]
        );
        // limit <= 0 -> the rest from offset.
        assert_eq!(cache.track_page_paths(3, 0).len(), 2);
        // Offset past the end -> empty.
        assert!(cache.track_page_paths(99, 10).is_empty());

        // Tag cache keyed by path.
        let track = |src: &str| Track {
            src: src.into(),
            title: "t".into(),
            ..Default::default()
        };
        assert!(cache.track_tags("/m/1.mp3").is_none());
        cache.put_track_tags(&[track("/m/1.mp3"), track("/m/2.mp3")]);
        assert_eq!(cache.track_tags("/m/1.mp3").unwrap().title, "t");
        // Drop marks only that path a miss again (re-read lazily).
        cache.drop_track_tags(&["/m/1.mp3".into()]);
        assert!(cache.track_tags("/m/1.mp3").is_none());
        assert!(
            cache.track_tags("/m/2.mp3").is_some(),
            "only the dropped path is affected"
        );

        // A reorder rewrites the index but leaves path-keyed tags intact.
        cache.replace_track_index(&["/m/2.mp3".into(), "/m/1.mp3".into()]);
        assert_eq!(cache.track_page_paths(0, 1), vec!["/m/2.mp3".to_string()]);
        assert!(
            cache.track_tags("/m/2.mp3").is_some(),
            "tags survive a reorder"
        );
    }

    #[test]
    fn synced_at_round_trips_and_clear_resets_tracks() {
        let cache = MetadataCache::new(temp_db("synced"));
        cache.reconcile(1);
        assert_eq!(cache.tracks_synced_at(), 0);
        cache.set_tracks_synced_at(12345);
        assert_eq!(cache.tracks_synced_at(), 12345);

        cache.replace_track_index(&["/x.mp3".into()]);
        cache.put_track_tags(&[Track {
            src: "/x.mp3".into(),
            ..Default::default()
        }]);
        // A library change clears index + tags + watermark (fingerprint gate kept).
        assert!(cache.reconcile(2), "new fingerprint clears");
        assert_eq!(cache.track_count(), 0);
        assert!(cache.track_tags("/x.mp3").is_none());
        assert_eq!(
            cache.tracks_synced_at(),
            0,
            "watermark reset on library change"
        );
    }

    #[test]
    fn disabled_db_transparently_falls_back() {
        // With no persistence, reconcile still validates but reads always miss
        // (so callers hit the provider). No panic.
        let cache = MetadataCache::new(Db::disabled());
        cache.reconcile(1);
        cache.put("k", &1u32);
        assert_eq!(cache.get::<u32>("k"), None);
    }
}
