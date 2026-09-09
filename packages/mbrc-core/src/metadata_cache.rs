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

use std::collections::{HashMap, HashSet};
use std::sync::atomic::{AtomicBool, Ordering};

use redb::{Durability, ReadableTable, ReadableTableMetadata};
use serde::de::DeserializeOwned;
use serde::{Deserialize, Serialize};

use crate::protocol::messages::{Track, TrackTags};
use crate::store::{
    ALBUM_STAMPS, Db, LIBRARY_FINGERPRINT, META, METADATA_CACHE, TAGS_SCHEMA, TRACK_PATHS,
    TRACK_SORT, TRACK_TAGS, TRACKS_SYNCED_AT,
};

/// The tags this cache keeps per track.
///
/// Not the wire `Track`: that one is serialized onto V4 frames, where its shape
/// is frozen. This record is the cache's own, and carries the typed fields the
/// V6 track schema and the sort orders need.
#[derive(Debug, Clone, Default, PartialEq, Serialize, Deserialize)]
pub struct CachedTags {
    pub src: String,
    pub artist: String,
    pub title: String,
    pub album: String,
    pub album_artist: String,
    pub track_no: i32,
    pub disc_no: i32,
    pub genre: String,
    /// Zero when the tag carried no year, which sorts before every real one.
    pub year: i32,
    pub rating: f32,
    /// ISO-8601 UTC, converted host-side; empty when unknown.
    pub date_added: String,
}

/// Bumped when [`CachedTags`] gains a field.
///
/// A record that has grown makes every stored row stale in a way no library
/// fingerprint would notice: the library did not change, the shape it is read
/// into did.
const TAGS_SCHEMA_VERSION: u32 = 2;

impl From<&TrackTags> for CachedTags {
    fn from(tags: &TrackTags) -> Self {
        Self {
            src: tags.src.clone(),
            artist: tags.artist.clone(),
            title: tags.title.clone(),
            album: tags.album.clone(),
            album_artist: tags.album_artist.clone(),
            track_no: tags.track_no,
            disc_no: tags.disc_no,
            genre: tags.genre.clone(),
            year: parse_year(&tags.year),
            rating: parse_rating(&tags.rating),
            date_added: tags.date_added.clone(),
        }
    }
}

/// A browse track as a sortable record, for ordering a list already in hand.
///
/// Partial by construction: the wire `Track` carries no year, rating or date
/// added, so what this produces is a record that only looks whole. It must
/// never be filed - the backfill fills rows that are missing, not rows that are
/// thin, so one stored here is one that stays wrong.
impl From<&Track> for CachedTags {
    fn from(track: &Track) -> Self {
        Self {
            src: track.src.clone(),
            artist: track.artist.clone(),
            title: track.title.clone(),
            album: track.album.clone(),
            album_artist: track.album_artist.clone(),
            track_no: track.trackno,
            disc_no: track.disc,
            genre: track.genre.clone(),
            ..Self::default()
        }
    }
}

impl From<&CachedTags> for Track {
    fn from(tags: &CachedTags) -> Self {
        Self {
            src: tags.src.clone(),
            artist: tags.artist.clone(),
            title: tags.title.clone(),
            album: tags.album.clone(),
            album_artist: tags.album_artist.clone(),
            trackno: tags.track_no,
            disc: tags.disc_no,
            genre: tags.genre.clone(),
        }
    }
}

/// [`METADATA_CACHE`] key holding the derived album -> year map.
const KEY_ALBUM_YEARS: &str = "album_years";

/// How an album is named across the cache: by the artist it is filed under.
///
/// The album artist when there is one, else the track artist - the same rule the
/// cover store keys on, so the two agree about what one album is.
pub fn album_key(album_artist: &str, artist: &str, album: &str) -> String {
    let filed_under = if album_artist.is_empty() {
        artist
    } else {
        album_artist
    };
    let mut key = filed_under.to_lowercase();
    key.push(' ');
    key.push_str(&album.to_lowercase());
    key
}

/// A name as a reader files it: case-folded, with a leading article dropped.
pub fn collate(name: &str) -> String {
    let lowered = name.trim().to_lowercase();
    for article in ["the ", "a ", "an "] {
        if let Some(rest) = lowered.strip_prefix(article) {
            return rest.trim_start().to_string();
        }
    }
    lowered
}

/// The leading four-digit year of a MusicBee year tag, which may be a full date.
fn parse_year(raw: &str) -> i32 {
    raw.split(['/', '-', ' ', '.'])
        .find_map(|part| {
            part.parse::<i32>()
                .ok()
                .filter(|y| (1000..=9999).contains(y))
        })
        .unwrap_or(0)
}

/// A rating tag, which may use either decimal separator.
fn parse_rating(raw: &str) -> f32 {
    raw.trim().replace(',', ".").parse::<f32>().unwrap_or(0.0)
}

/// An order a track list can be read in.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub enum SortField {
    Title,
    Artist,
    Album,
    AlbumArtist,
    Track,
    Year,
    Rating,
    DateAdded,
}

impl SortField {
    pub const ALL: [SortField; 8] = [
        SortField::Title,
        SortField::Artist,
        SortField::Album,
        SortField::AlbumArtist,
        SortField::Track,
        SortField::Year,
        SortField::Rating,
        SortField::DateAdded,
    ];

    /// The wire spelling, which is also the key this order is stored under.
    pub fn as_str(self) -> &'static str {
        match self {
            SortField::Title => "title",
            SortField::Artist => "artist",
            SortField::Album => "album",
            SortField::AlbumArtist => "album_artist",
            SortField::Track => "track",
            SortField::Year => "year",
            SortField::Rating => "rating",
            SortField::DateAdded => "date_added",
        }
    }

    pub fn parse(value: &str) -> Option<Self> {
        Self::ALL.into_iter().find(|f| f.as_str() == value)
    }

    /// Orders two tracks, falling back to the natural reading order of an album
    /// so that equal keys do not come back shuffled between requests.
    pub fn compare(self, a: &CachedTags, b: &CachedTags) -> std::cmp::Ordering {
        let by_name = |x: &str, y: &str| collate(x).cmp(&collate(y));
        let then_track = |ord: std::cmp::Ordering| {
            ord.then_with(|| (a.disc_no, a.track_no).cmp(&(b.disc_no, b.track_no)))
                .then_with(|| by_name(&a.title, &b.title))
        };
        match self {
            SortField::Title => by_name(&a.title, &b.title),
            SortField::Artist => then_track(by_name(&a.artist, &b.artist)),
            SortField::Album => then_track(by_name(&a.album, &b.album)),
            SortField::AlbumArtist => then_track(by_name(&a.album_artist, &b.album_artist)),
            SortField::Track => then_track(std::cmp::Ordering::Equal),
            SortField::Year => then_track(a.year.cmp(&b.year)),
            SortField::Rating => then_track(
                a.rating
                    .partial_cmp(&b.rating)
                    .unwrap_or(std::cmp::Ordering::Equal),
            ),
            SortField::DateAdded => then_track(a.date_added.cmp(&b.date_added)),
        }
    }
}

/// What narrows a set of tracks: a search, and the scope it was reached through.
#[derive(Debug, Default, Clone, Copy)]
pub struct TrackFilter<'a> {
    /// Case-insensitive substring over the fields the level shows.
    pub query: Option<&'a str>,
    pub artist: Option<&'a str>,
    pub album: Option<&'a str>,
    pub genre: Option<&'a str>,
}

/// Whether a track is filed under `artist`.
///
/// The artist list is built from either tag depending on the album-artists
/// setting, so a scope has to admit both. The empty name is the exception and
/// has to mean untagged: a track that merely lacks an album artist is a normal
/// record, and admitting it would put most of a library in the group that is
/// meant to hold the ones with no name at all.
fn filed_under(track: &CachedTags, artist: &str) -> bool {
    if artist.is_empty() {
        return track.artist.is_empty() && track.album_artist.is_empty();
    }
    equals(&track.artist, artist) || equals(&track.album_artist, artist)
}

fn contains(haystack: &str, needle: &str) -> bool {
    haystack.to_lowercase().contains(&needle.to_lowercase())
}

fn equals(a: &str, b: &str) -> bool {
    a.eq_ignore_ascii_case(b)
}

/// The file name of a path, which is what an untagged track is known by.
fn file_name(path: &str) -> &str {
    path.rsplit(['/', '\\']).next().unwrap_or(path)
}

/// How well a name answers a search: lower is a better answer.
///
/// Browse order within a match set buries what was asked for - searching for a
/// title puts it below every track that merely contains the word.
pub fn relevance(name: &str, needle: &str) -> u8 {
    let name = name.to_lowercase();
    if name == needle {
        return 0;
    }
    if name.starts_with(needle) {
        return 1;
    }
    if collate(&name).starts_with(needle) {
        return 2;
    }
    if name.split_whitespace().any(|word| word.starts_with(needle)) {
        return 3;
    }
    4
}

impl TrackFilter<'_> {
    /// The best answer a track gives to the search, over the fields it matched.
    fn relevance_of(&self, track: Option<&CachedTags>, path: &str) -> u8 {
        let Some(needle) = self.query else { return 0 };
        match track {
            Some(t) => relevance(&t.title, needle).min(relevance(&t.artist, needle)),
            None => relevance(file_name(path), needle),
        }
    }

    fn admits(&self, track: Option<&CachedTags>, path: &str) -> bool {
        let Some(track) = track else {
            return self.artist.is_none()
                && self.album.is_none()
                && self.genre.is_none()
                && self.query.is_some_and(|q| contains(file_name(path), q));
        };
        self.artist.is_none_or(|a| filed_under(track, a))
            && self.album.is_none_or(|al| equals(&track.album, al))
            && self.genre.is_none_or(|g| equals(&track.genre, g))
            && self
                .query
                .is_none_or(|q| contains(&track.title, q) || contains(&track.artist, q))
    }
}

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

    /// Reads a cached response by key, deserialized to `T`. `None` when the cache
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

    /// Caches a response under `key`. No-op when disabled or not validated.
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

    /// Drops every cached entry (used on a library change): the generic blob cache
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
            txn.delete_table(TRACK_SORT)?;
            txn.delete_table(ALBUM_STAMPS)?;
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

    // ── Track ordinal index + path-keyed tag cache ──
    //
    // Tracks would OOM the 32-bit core as one blob, so it is an index plus a
    // tag cache, behind the same `validated` gate as above.

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

    /// Replaces the ordinal index with `paths` in browse order (positions `0..n`).
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

    /// The cached tags for a path, if present. `None` when disabled,
    /// unvalidated, on a miss, or a decode error - the caller then fills it via
    /// one FFI batch and writes it back with [`put_track_tags`](Self::put_track_tags).
    pub fn track_tags(&self, path: &str) -> Option<CachedTags> {
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

    /// Caches the given tracks, keyed by each track's `src` path, in one write
    /// transaction. No-op when disabled, not validated, or empty.
    pub fn put_track_tags(&self, tracks: &[CachedTags]) {
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

    /// Drops cached tags for these paths (a delta marked them changed; they are
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

    /// Records the tracks-cache sync watermark (unix seconds).
    pub fn set_tracks_synced_at(&self, ts: i64) {
        self.db.write(Durability::Immediate, |txn| {
            let mut table = txn.open_table(META)?;
            table.insert(TRACKS_SYNCED_AT, ts.to_le_bytes().as_slice())?;
            Ok(())
        });
    }

    /// Reconciles the stored library against the current one.
    ///
    /// The fingerprint says whether anything moved; the per-album stamps say
    /// which albums did, so only their tag rows are dropped. A rating written
    /// to one record must not cost the other fifteen thousand, which then take
    /// a full backfill to read again.
    ///
    /// Returns whether the library moved, which the browse lists rebuild on.
    pub fn reconcile(&self, albums: &[(String, i64)], fingerprint: u64) -> bool {
        let restaled = self.tag_record_changed_shape();
        if restaled {
            self.clear();
            self.store_tags_schema();
        }

        let moved = restaled || self.stored_fingerprint() != Some(fingerprint);
        if moved {
            if !restaled {
                self.drop_moved_albums(albums);
            }
            self.store_fingerprint(fingerprint);
        }
        if moved || !self.has_album_stamps() {
            self.store_album_stamps(albums);
        }
        self.set_validated(true);
        moved
    }

    /// Whether [`CachedTags`] has gained fields since the store was written.
    ///
    /// Such a change makes every stored row stale in a way no stamp would
    /// notice: the library did not change, the shape it is read into did.
    fn tag_record_changed_shape(&self) -> bool {
        self.stored_tags_schema() != Some(TAGS_SCHEMA_VERSION)
    }

    /// The indexed paths that have no cached tags yet, in browse order.
    ///
    /// The backfill's work list. Reads the ordinal index rather than the tag
    /// table so a track added to the library shows up as missing.
    pub fn untagged_paths(&self, limit: usize) -> Vec<String> {
        if !self.is_validated() || limit == 0 {
            return Vec::new();
        }
        self.db
            .read(|txn| {
                let paths = match txn.open_table(TRACK_PATHS) {
                    Ok(paths) => paths,
                    Err(_) => return Ok(Vec::new()),
                };
                let tags = txn.open_table(TRACK_TAGS).ok();
                let mut missing = Vec::new();
                for row in paths.iter()? {
                    let (_pos, path) = row?;
                    let path = path.value();
                    let known = tags
                        .as_ref()
                        .is_some_and(|table| matches!(table.get(path), Ok(Some(_))));
                    if !known {
                        missing.push(path.to_string());
                        if missing.len() >= limit {
                            break;
                        }
                    }
                }
                Ok(missing)
            })
            .unwrap_or_default()
    }

    /// Every cached tag row, for building an order or a derived map.
    pub fn all_cached_tags(&self) -> Vec<CachedTags> {
        if !self.is_validated() {
            return Vec::new();
        }
        self.db
            .read(|txn| {
                let table = match txn.open_table(TRACK_TAGS) {
                    Ok(t) => t,
                    Err(_) => return Ok(Vec::new()),
                };
                let mut all = Vec::new();
                for row in table.iter()? {
                    let (_path, bytes) = row?;
                    if let Ok(tags) = rmp_serde::from_slice::<CachedTags>(bytes.value()) {
                        all.push(tags);
                    }
                }
                Ok(all)
            })
            .unwrap_or_default()
    }

    /// The indexed paths a filter admits, best answer first.
    ///
    /// A scan rather than a range: matching is on tag content, which the
    /// ordinal index says nothing about, and every match is returned so the
    /// caller can report a true `total` or queue the whole scope.
    ///
    /// Both sorts are stable, so an order asked for reorders the matches while
    /// the best answer stays first among the rows it cannot tell apart.
    pub fn track_paths_where(
        &self,
        filter: &TrackFilter<'_>,
        sort: Option<SortField>,
        descending: bool,
    ) -> Vec<String> {
        if !self.is_validated() {
            return Vec::new();
        }
        let mut matched: Vec<(u8, Option<CachedTags>, String)> = self
            .db
            .read(|txn| {
                let paths = match txn.open_table(TRACK_PATHS) {
                    Ok(p) => p,
                    Err(_) => return Ok(Vec::new()),
                };
                let tags = txn.open_table(TRACK_TAGS).ok();
                let mut matched = Vec::new();
                for row in paths.iter()? {
                    let (_pos, path) = row?;
                    let path = path.value();
                    let cached = tags
                        .as_ref()
                        .and_then(|t| t.get(path).ok().flatten())
                        .and_then(|g| rmp_serde::from_slice::<CachedTags>(g.value()).ok());
                    if filter.admits(cached.as_ref(), path) {
                        let rank = filter.relevance_of(cached.as_ref(), path);
                        matched.push((rank, cached, path.to_string()));
                    }
                }
                Ok(matched)
            })
            .unwrap_or_default();

        matched.sort_by_key(|(rank, _, _)| *rank);
        if let Some(field) = sort {
            matched.sort_by(|(_, a, _), (_, b, _)| match (a, b) {
                (Some(a), Some(b)) => field.compare(a, b),
                // A row with no tags cannot be ordered by one; it goes last.
                (Some(_), None) => std::cmp::Ordering::Less,
                (None, Some(_)) => std::cmp::Ordering::Greater,
                (None, None) => std::cmp::Ordering::Equal,
            });
            if descending {
                matched.reverse();
            }
        }
        matched.into_iter().map(|(_, _, path)| path).collect()
    }

    /// Rebuilds every sort order from the cached tags. Returns rows indexed.
    ///
    /// Only ascending is stored: descending is the same range walked backwards.
    pub fn rebuild_sort_orders(&self) -> usize {
        let all = self.all_cached_tags();
        if all.is_empty() {
            return 0;
        }
        let mut indexed = 0;
        for field in SortField::ALL {
            let mut sorted: Vec<&CachedTags> = all.iter().collect();
            sorted.sort_by(|a, b| field.compare(a, b));
            self.db.write(Durability::Immediate, |txn| {
                let mut table = txn.open_table(TRACK_SORT)?;
                for (position, tags) in sorted.iter().enumerate() {
                    table.insert((field.as_str(), position as u32), tags.src.as_str())?;
                }
                Ok(())
            });
            indexed = sorted.len();
        }
        indexed
    }

    /// How many rows one order holds, which says whether it is complete.
    pub fn sorted_track_count(&self, field: SortField) -> u64 {
        if !self.is_validated() {
            return 0;
        }
        self.db
            .read(|txn| {
                let table = match txn.open_table(TRACK_SORT) {
                    Ok(t) => t,
                    Err(_) => return Ok(0),
                };
                let mut count = 0u64;
                for row in table.range((field.as_str(), 0u32)..(field.as_str(), u32::MAX))? {
                    row?;
                    count += 1;
                }
                Ok(count)
            })
            .unwrap_or(0)
    }

    /// One page of a stored order.
    ///
    /// Descending is the mirror window read backwards rather than a second
    /// stored order: the last page ascending is the first page descending.
    pub fn sorted_track_page(
        &self,
        field: SortField,
        offset: i32,
        limit: i32,
        descending: bool,
    ) -> Vec<String> {
        if !self.is_validated() {
            return Vec::new();
        }
        let total = self.sorted_track_count(field) as i64;
        let offset = offset.max(0) as i64;
        let limit = if limit <= 0 {
            (total - offset).max(0)
        } else {
            limit as i64
        };
        let start = if descending {
            (total - offset - limit).max(0)
        } else {
            offset
        };
        let end = if descending {
            (total - offset).max(0)
        } else {
            (offset + limit).min(total)
        };
        if start >= end {
            return Vec::new();
        }
        let mut page = self
            .db
            .read(|txn| {
                let table = match txn.open_table(TRACK_SORT) {
                    Ok(t) => t,
                    Err(_) => return Ok(Vec::new()),
                };
                let mut paths = Vec::new();
                for row in
                    table.range((field.as_str(), start as u32)..(field.as_str(), end as u32))?
                {
                    let (_key, path) = row?;
                    paths.push(path.value().to_string());
                }
                Ok(paths)
            })
            .unwrap_or_default();
        if descending {
            page.reverse();
        }
        page
    }

    /// Derives each album's year from its own tracks. Returns albums dated.
    ///
    /// The earliest non-zero year its tracks carry, because a reissued track
    /// stamped with the reissue year should not redate the record.
    pub fn rebuild_album_years(&self) -> usize {
        let mut years: HashMap<String, i32> = HashMap::new();
        for tags in self.all_cached_tags() {
            if tags.year <= 0 {
                continue;
            }
            let key = album_key(&tags.album_artist, &tags.artist, &tags.album);
            years
                .entry(key)
                .and_modify(|y| *y = (*y).min(tags.year))
                .or_insert(tags.year);
        }
        let dated = years.len();
        self.put(KEY_ALBUM_YEARS, &years);
        dated
    }

    /// The derived album -> year map, empty until it has been built.
    pub fn album_years(&self) -> HashMap<String, i32> {
        self.get(KEY_ALBUM_YEARS).unwrap_or_default()
    }

    /// The stored cached-tag schema version, if one has been written.
    fn stored_tags_schema(&self) -> Option<u32> {
        self.db
            .read(|txn| {
                let table = match txn.open_table(META) {
                    Ok(t) => t,
                    Err(redb::TableError::TableDoesNotExist(_)) => return Ok(None),
                    Err(e) => return Err(e.into()),
                };
                Ok(table
                    .get(TAGS_SCHEMA)?
                    .and_then(|v| <[u8; 4]>::try_from(v.value()).ok().map(u32::from_le_bytes)))
            })
            .flatten()
    }

    fn store_tags_schema(&self) {
        self.db.write(Durability::Immediate, |txn| {
            let mut table = txn.open_table(META)?;
            table.insert(TAGS_SCHEMA, TAGS_SCHEMA_VERSION.to_le_bytes().as_slice())?;
            Ok(())
        });
    }

    /// Drops the cached tags of every album whose stamp moved or vanished.
    ///
    /// With no stamps stored there is nothing to compare against - a cache
    /// written before stamps existed - so the whole of it is dropped once,
    /// which is the behaviour this replaces.
    fn drop_moved_albums(&self, albums: &[(String, i64)]) {
        let stored = self.stored_album_stamps();
        if stored.is_empty() {
            self.clear();
            return;
        }

        let current: HashMap<&str, i64> = albums
            .iter()
            .map(|(key, modified)| (key.as_str(), *modified))
            .collect();
        let mut stale: Vec<&str> = stored
            .iter()
            .filter(|(key, was)| current.get(key.as_str()) != Some(was))
            .map(|(key, _)| key.as_str())
            .collect();
        stale.extend(
            albums
                .iter()
                .filter(|(key, _)| !stored.contains_key(key))
                .map(|(key, _)| key.as_str()),
        );
        if stale.is_empty() {
            return;
        }

        let stale: HashSet<&str> = stale.into_iter().collect();
        self.drop_tags_of(&stale);
    }

    /// Removes the tag rows belonging to the named albums, and any order built
    /// from them: a sort order missing the rows it indexed would page a reader
    /// through tracks the cache can no longer describe.
    fn drop_tags_of(&self, albums: &HashSet<&str>) {
        let doomed: Vec<String> = self
            .db
            .read(|txn| {
                let table = match txn.open_table(TRACK_TAGS) {
                    Ok(t) => t,
                    Err(_) => return Ok(Vec::new()),
                };
                let mut doomed = Vec::new();
                for row in table.iter()? {
                    let (path, bytes) = row?;
                    let Ok(tags) = rmp_serde::from_slice::<CachedTags>(bytes.value()) else {
                        continue;
                    };
                    let key = album_key(&tags.album_artist, &tags.artist, &tags.album);
                    if albums.contains(key.as_str()) {
                        doomed.push(path.value().to_string());
                    }
                }
                Ok(doomed)
            })
            .unwrap_or_default();

        if doomed.is_empty() {
            return;
        }
        self.db.write(Durability::Immediate, |txn| {
            let mut table = txn.open_table(TRACK_TAGS)?;
            for path in &doomed {
                table.remove(path.as_str())?;
            }
            txn.delete_table(TRACK_SORT)?;
            let mut derived = txn.open_table(METADATA_CACHE)?;
            derived.remove(KEY_ALBUM_YEARS)?;
            Ok(())
        });
    }

    fn stored_album_stamps(&self) -> HashMap<String, i64> {
        self.db
            .read(|txn| {
                let table = match txn.open_table(ALBUM_STAMPS) {
                    Ok(t) => t,
                    Err(_) => return Ok(HashMap::new()),
                };
                let mut stamps = HashMap::new();
                for row in table.iter()? {
                    let (key, modified) = row?;
                    stamps.insert(key.value().to_string(), modified.value());
                }
                Ok(stamps)
            })
            .unwrap_or_default()
    }

    /// Records what a later change is diffed against.
    ///
    /// Written whenever they are missing, not only when something moved: a
    /// cache that keeps starting up unchanged would never record any, and the
    /// first real change would find nothing to compare and drop the library.
    fn store_album_stamps(&self, albums: &[(String, i64)]) {
        self.db.write(Durability::Immediate, |txn| {
            txn.delete_table(ALBUM_STAMPS)?;
            let mut table = txn.open_table(ALBUM_STAMPS)?;
            for (key, modified) in albums {
                table.insert(key.as_str(), *modified)?;
            }
            Ok(())
        });
    }

    fn has_album_stamps(&self) -> bool {
        self.db
            .read(|txn| match txn.open_table(ALBUM_STAMPS) {
                Ok(table) => Ok(table.len().unwrap_or(0) > 0),
                Err(_) => Ok(false),
            })
            .unwrap_or(false)
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
        assert!(cache.reconcile(&[], 42));
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
        cache.reconcile(&[], 1);
        assert!(!cache.contains("k"), "absent key");
        cache.put("k", &vec![1u32, 2, 3]);
        assert!(cache.contains("k"), "present after put");
        // A library change clears the table, so presence drops.
        cache.reconcile(&[], 2);
        assert!(!cache.contains("k"), "cleared on fingerprint change");
    }

    /// A blank album artist is ordinary - MusicBee leaves it so for singles -
    /// while a blank artist name is not. Admitting either would file most of a
    /// library under the group meant for records with no name at all.
    #[test]
    fn the_untagged_artist_holds_only_records_with_neither_name() {
        let named = CachedTags {
            src: "/a.mp3".into(),
            artist: "Adele".into(),
            ..CachedTags::default()
        };
        let untagged = CachedTags {
            src: "/b.mp3".into(),
            ..CachedTags::default()
        };
        let compilation = CachedTags {
            src: "/c.mp3".into(),
            album_artist: "Various Artists".into(),
            ..CachedTags::default()
        };

        assert!(filed_under(&untagged, ""), "neither name: untagged");
        assert!(
            !filed_under(&named, ""),
            "a blank album artist is not an untagged record"
        );
        assert!(
            !filed_under(&compilation, ""),
            "a blank artist under a named album artist is filed under that name"
        );
        assert!(
            filed_under(&named, "adele"),
            "a name still admits either tag"
        );
        assert!(filed_under(&compilation, "Various Artists"));
    }

    #[test]
    fn reconcile_clears_only_on_fingerprint_change() {
        let cache = MetadataCache::new(temp_db("recon"));
        assert!(
            cache.reconcile(&[], 1),
            "first fingerprint counts as a change"
        );
        cache.put("k", &7u32);
        assert!(!cache.reconcile(&[], 1), "same fingerprint: cache kept");
        assert_eq!(cache.get::<u32>("k"), Some(7));
        assert!(cache.reconcile(&[], 2), "new fingerprint: change");
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

    /// A cache holding five tracks and the tags for two of them.
    fn indexed_cache(name: &str) -> MetadataCache {
        let cache = MetadataCache::new(temp_db(name));
        cache.reconcile(&[], 1);
        let paths: Vec<String> = (0..5).map(|i| format!("/m/{i}.mp3")).collect();
        cache.replace_track_index(&paths);
        cache
    }

    fn tagged(src: &str) -> CachedTags {
        CachedTags {
            src: src.into(),
            title: "t".into(),
            ..Default::default()
        }
    }

    #[test]
    fn the_track_index_is_gated_until_the_cache_is_validated() {
        let cache = MetadataCache::new(temp_db("tracks"));
        cache.replace_track_index(&["a".into(), "b".into()]);
        assert_eq!(cache.track_count(), 0);
        assert!(cache.track_page_paths(0, 10).is_empty());
    }

    #[test]
    fn a_page_is_served_from_the_index_in_order() {
        let cache = indexed_cache("paging");
        assert_eq!(cache.track_count(), 5);
        assert_eq!(
            cache.track_page_paths(1, 2),
            vec!["/m/1.mp3".to_string(), "/m/2.mp3".to_string()]
        );
    }

    #[test]
    fn a_limit_of_zero_takes_the_rest_from_the_offset() {
        assert_eq!(indexed_cache("limit").track_page_paths(3, 0).len(), 2);
    }

    #[test]
    fn an_offset_past_the_end_is_empty() {
        assert!(indexed_cache("offset").track_page_paths(99, 10).is_empty());
    }

    #[test]
    fn tags_are_cached_by_path() {
        let cache = indexed_cache("tags");
        assert!(cache.track_tags("/m/1.mp3").is_none());
        cache.put_track_tags(&[tagged("/m/1.mp3")]);
        assert_eq!(cache.track_tags("/m/1.mp3").unwrap().title, "t");
    }

    #[test]
    fn dropping_tags_affects_only_the_dropped_path() {
        let cache = indexed_cache("drop");
        cache.put_track_tags(&[tagged("/m/1.mp3"), tagged("/m/2.mp3")]);
        cache.drop_track_tags(&["/m/1.mp3".into()]);
        assert!(cache.track_tags("/m/1.mp3").is_none());
        assert!(cache.track_tags("/m/2.mp3").is_some());
    }

    #[test]
    fn a_reorder_rewrites_the_index_and_keeps_the_tags() {
        let cache = indexed_cache("reorder");
        cache.put_track_tags(&[tagged("/m/2.mp3")]);
        cache.replace_track_index(&["/m/2.mp3".into(), "/m/1.mp3".into()]);
        assert_eq!(cache.track_page_paths(0, 1), vec!["/m/2.mp3".to_string()]);
        assert!(cache.track_tags("/m/2.mp3").is_some());
    }

    #[test]
    fn synced_at_round_trips_and_clear_resets_tracks() {
        let cache = MetadataCache::new(temp_db("synced"));
        cache.reconcile(&[], 1);
        assert_eq!(cache.tracks_synced_at(), 0);
        cache.set_tracks_synced_at(12345);
        assert_eq!(cache.tracks_synced_at(), 12345);

        cache.replace_track_index(&["/x.mp3".into()]);
        cache.put_track_tags(&[CachedTags {
            src: "/x.mp3".into(),
            ..Default::default()
        }]);
        assert!(
            cache.reconcile(&[], 2),
            "a new fingerprint clears the cache"
        );
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
        cache.reconcile(&[], 1);
        cache.put("k", &1u32);
        assert_eq!(cache.get::<u32>("k"), None);
    }

    #[test]
    fn a_year_tag_that_is_a_whole_date_still_yields_its_year() {
        assert_eq!(parse_year("2007"), 2007);
        assert_eq!(parse_year("12/03/2007"), 2007);
        assert_eq!(parse_year("2007-03-12"), 2007);
        // Nothing four-digit to take, so the album is undated rather than wrong.
        assert_eq!(parse_year(""), 0);
        assert_eq!(parse_year("07"), 0);
        assert_eq!(parse_year("unknown"), 0);
    }

    /// MusicBee reports a rating in the running culture, so half a star arrives
    /// with either separator.
    #[test]
    fn a_rating_parses_under_either_decimal_separator() {
        assert_eq!(parse_rating("3.5"), 3.5);
        assert_eq!(parse_rating("3,5"), 3.5);
        assert_eq!(parse_rating(""), 0.0);
        assert_eq!(parse_rating("none"), 0.0);
    }

    /// Alphabetical order inside a match set buries what was asked for: three
    /// bands have an album called Live, and the one that IS "live" has to come
    /// before every title that merely contains the word.
    #[test]
    fn a_search_answers_with_its_best_match_first() {
        let mut hits = [
            "Live After Death",
            "Alive",
            "Live",
            "The Live Album",
            "Livewire",
        ];
        hits.sort_by_key(|name| relevance(name, "live"));

        assert_eq!(
            hits,
            [
                // the name that is the search
                "Live",
                // then the ones it starts
                "Live After Death",
                "Livewire",
                // then one whose filed name starts with it
                "The Live Album",
                // then the rest
                "Alive",
            ]
        );
    }

    #[test]
    fn relevance_is_case_insensitive_and_ranks_a_later_word() {
        assert_eq!(relevance("LIVE", "live"), 0);
        assert!(relevance("Death or Live", "live") < relevance("Alive", "live"));
    }

    /// A reader looks for Beatles under B, not under T.
    #[test]
    fn names_collate_the_way_they_are_filed() {
        assert_eq!(collate("The Beatles"), "beatles");
        assert_eq!(collate("A Perfect Circle"), "perfect circle");
        assert_eq!(collate("An Evening"), "evening");
        assert_eq!(collate("  Iron Maiden "), "iron maiden");
        // Only a whole leading article, never a prefix of a word.
        assert_eq!(collate("Theatre of Tragedy"), "theatre of tragedy");
        assert_eq!(collate("Anathema"), "anathema");
    }

    /// One album is one album however its tracks spell the artist, and however
    /// the album artist tag is filled in.
    #[test]
    fn an_album_key_is_the_artist_it_is_filed_under() {
        assert_eq!(
            album_key("Bob Mould", "Bob Mould", "Silver Age"),
            album_key("BOB MOULD", "x", "SILVER AGE")
        );
        // No album artist: the track artist files it.
        assert_eq!(
            album_key("", "Bob Mould", "Silver Age"),
            album_key("Bob Mould", "", "Silver Age")
        );
        assert_ne!(
            album_key("Queen", "Queen", "Live"),
            album_key("AC/DC", "AC/DC", "Live")
        );
    }

    fn track(src: &str, title: &str, year: i32) -> CachedTags {
        CachedTags {
            src: src.into(),
            title: title.into(),
            artist: "Artist".into(),
            album_artist: "Artist".into(),
            album: "Album".into(),
            year,
            ..CachedTags::default()
        }
    }

    fn sorted_cache(name: &str, tracks: &[CachedTags]) -> MetadataCache {
        let cache = MetadataCache::new(temp_db(name));
        cache.reconcile(&[], 1);
        cache.replace_track_index(
            &tracks
                .iter()
                .map(|t| t.src.clone())
                .collect::<Vec<String>>(),
        );
        cache.put_track_tags(tracks);
        cache.rebuild_sort_orders();
        cache
    }

    #[test]
    fn an_order_is_stored_once_and_read_from_either_end() {
        let cache = sorted_cache(
            "sorted",
            &[
                track("/c.mp3", "C", 2001),
                track("/a.mp3", "A", 1999),
                track("/b.mp3", "B", 2000),
            ],
        );
        assert_eq!(cache.sorted_track_count(SortField::Year), 3);

        assert_eq!(
            cache.sorted_track_page(SortField::Year, 0, 3, false),
            vec!["/a.mp3".to_string(), "/b.mp3".into(), "/c.mp3".into()],
        );
        // Descending is the same order walked backwards, not a second one.
        assert_eq!(
            cache.sorted_track_page(SortField::Year, 0, 3, true),
            vec!["/c.mp3".to_string(), "/b.mp3".into(), "/a.mp3".into()],
        );
    }

    /// The window has to mirror, not just the page contents: page one descending
    /// is the last page ascending, or paging walks the list twice.
    #[test]
    fn a_descending_window_mirrors_the_ascending_one() {
        let tracks: Vec<CachedTags> = (0..10)
            .map(|i| track(&format!("/{i}.mp3"), &format!("T{i}"), 2000 + i))
            .collect();
        let cache = sorted_cache("sorted-window", &tracks);

        let desc = cache.sorted_track_page(SortField::Year, 2, 3, true);
        let mut asc = cache.sorted_track_page(SortField::Year, 10 - 2 - 3, 3, false);
        asc.reverse();
        assert_eq!(desc, asc);
        assert_eq!(
            desc,
            vec!["/7.mp3".to_string(), "/6.mp3".into(), "/5.mp3".into()]
        );
    }

    /// An album takes the earliest year its tracks carry: one track stamped with
    /// a reissue year must not redate the record.
    #[test]
    fn an_album_is_dated_by_the_earliest_year_its_tracks_carry() {
        let cache = sorted_cache(
            "years",
            &[
                track("/a.mp3", "A", 2012),
                track("/b.mp3", "B", 2020),
                // Undated tracks say nothing rather than dating the album to 0.
                track("/c.mp3", "C", 0),
            ],
        );
        let dated = cache.rebuild_album_years();

        assert_eq!(dated, 1);
        assert_eq!(
            cache
                .album_years()
                .get(&album_key("Artist", "Artist", "Album")),
            Some(&2012)
        );
    }

    /// Search matches tag content, which the ordinal index says nothing about,
    /// so it scans - and a track with no tags yet is matched on its file name
    /// rather than dropped.
    #[test]
    fn a_filter_matches_tags_and_falls_back_to_the_file_name() {
        let cache = MetadataCache::new(temp_db("filter"));
        cache.reconcile(&[], 1);
        cache.replace_track_index(&["/tagged.mp3".into(), "/untagged-gem.mp3".into()]);
        cache.put_track_tags(&[track("/tagged.mp3", "Emerald Sword", 1998)]);

        assert_eq!(
            cache.track_paths_where(
                &TrackFilter {
                    query: Some("emerald"),
                    ..Default::default()
                },
                None,
                false
            ),
            vec!["/tagged.mp3".to_string()]
        );
        assert_eq!(
            cache.track_paths_where(
                &TrackFilter {
                    query: Some("gem"),
                    ..Default::default()
                },
                None,
                false
            ),
            vec!["/untagged-gem.mp3".to_string()],
            "an untagged track is still findable by its name"
        );
        // A scope names something only tags can answer, so an untagged track
        // cannot be admitted into it.
        assert!(
            cache
                .track_paths_where(
                    &TrackFilter {
                        artist: Some("Artist"),
                        ..Default::default()
                    },
                    None,
                    false
                )
                .iter()
                .all(|p| p == "/tagged.mp3")
        );
    }

    #[test]
    fn the_backfill_is_offered_only_what_has_no_tags() {
        let cache = MetadataCache::new(temp_db("untagged"));
        cache.reconcile(&[], 1);
        cache.replace_track_index(&["/a.mp3".into(), "/b.mp3".into(), "/c.mp3".into()]);
        cache.put_track_tags(&[track("/b.mp3", "B", 2000)]);

        assert_eq!(
            cache.untagged_paths(10),
            vec!["/a.mp3".to_string(), "/c.mp3".into()]
        );
        assert_eq!(
            cache.untagged_paths(1),
            vec!["/a.mp3".to_string()],
            "limited"
        );
        assert!(cache.untagged_paths(0).is_empty());
    }

    /// Builds a cache holding one track of each named album, tagged and indexed.
    fn cache_with_albums(name: &str, albums: &[(&str, i64)]) -> MetadataCache {
        let cache = MetadataCache::new(temp_db(name));
        let stamps: Vec<(String, i64)> = albums
            .iter()
            .map(|(album, modified)| (album_key("Artist", "Artist", album), *modified))
            .collect();
        cache.reconcile(
            &stamps,
            fingerprint(stamps.iter().map(|(k, m)| (k.as_str(), *m))),
        );

        let tracks: Vec<CachedTags> = albums
            .iter()
            .map(|(album, _)| CachedTags {
                src: format!("C:/{album}.mp3"),
                artist: "Artist".into(),
                album_artist: "Artist".into(),
                album: (*album).into(),
                title: (*album).into(),
                ..CachedTags::default()
            })
            .collect();
        cache.replace_track_index(
            &tracks
                .iter()
                .map(|t| t.src.clone())
                .collect::<Vec<String>>(),
        );
        cache.put_track_tags(&tracks);
        cache
    }

    /// One record's mtime moves whenever a rating or a play count is written to
    /// it. Reading the whole library again for that is a half-hour backfill, so
    /// the album that moved is the only one that loses its tags.
    #[test]
    fn only_the_album_that_moved_loses_its_tags() {
        let cache = cache_with_albums("stamps", &[("one", 100), ("two", 200)]);
        assert!(cache.untagged_paths(10).is_empty(), "both albums tagged");

        let moved: Vec<(String, i64)> = vec![
            (album_key("Artist", "Artist", "one"), 100),
            (album_key("Artist", "Artist", "two"), 999),
        ];
        assert!(cache.reconcile(
            &moved,
            fingerprint(moved.iter().map(|(k, m)| (k.as_str(), *m)))
        ));

        assert_eq!(
            cache.untagged_paths(10),
            vec!["C:/two.mp3".to_string()],
            "only the moved album is refetched"
        );
    }

    #[test]
    fn an_album_that_is_gone_takes_its_rows_with_it() {
        let cache = cache_with_albums("stamps-gone", &[("one", 100), ("two", 200)]);
        let left: Vec<(String, i64)> = vec![(album_key("Artist", "Artist", "one"), 100)];
        cache.reconcile(
            &left,
            fingerprint(left.iter().map(|(k, m)| (k.as_str(), *m))),
        );

        // The ordinal index still lists both, so the dropped row shows up as one
        // needing tags rather than as a row nothing can describe.
        assert_eq!(cache.untagged_paths(10), vec!["C:/two.mp3".to_string()]);
    }

    /// Stamps have to be recorded on an ordinary start, not only on a change.
    ///
    /// A cache carried over from before stamps existed matches its fingerprint
    /// every time it starts, so a version that wrote them only when something
    /// moved never wrote any - and the first real change found nothing to diff
    /// and dropped the whole library.
    #[test]
    fn a_quiet_start_still_records_what_a_later_change_is_diffed_against() {
        let cache = cache_with_albums("stamps-quiet", &[("one", 100), ("two", 200)]);
        let albums: Vec<(String, i64)> = vec![
            (album_key("Artist", "Artist", "one"), 100),
            (album_key("Artist", "Artist", "two"), 200),
        ];
        let same = fingerprint(albums.iter().map(|(k, m)| (k.as_str(), *m)));

        // The state an upgraded cache is in: a fingerprint, and no stamps.
        cache.db.write(Durability::Immediate, |txn| {
            txn.delete_table(ALBUM_STAMPS)?;
            Ok(())
        });
        cache.store_fingerprint(same);

        assert!(
            !cache.reconcile(&albums, same),
            "a quiet start changes nothing"
        );

        // Now one record is rated, which moves its file and so its stamp.
        let moved: Vec<(String, i64)> = vec![
            (album_key("Artist", "Artist", "one"), 100),
            (album_key("Artist", "Artist", "two"), 999),
        ];
        assert!(cache.reconcile(
            &moved,
            fingerprint(moved.iter().map(|(k, m)| (k.as_str(), *m)))
        ));

        assert_eq!(
            cache.untagged_paths(10),
            vec!["C:/two.mp3".to_string()],
            "the album that moved, not the whole library"
        );
    }

    /// A cache written before stamps existed has nothing to compare against, so
    /// it is dropped once rather than trusted.
    #[test]
    fn a_cache_with_no_stamps_is_dropped_once() {
        let cache = MetadataCache::new(temp_db("stamps-cold"));
        cache.reconcile(&[], 1);
        cache.put("k", &7u32);

        let albums = vec![(album_key("Artist", "Artist", "one"), 100)];
        assert!(cache.reconcile(&albums, 2));
        assert_eq!(cache.get::<u32>("k"), None, "dropped with nothing to diff");

        cache.put("k", &7u32);
        let same = albums.clone();
        assert!(cache.reconcile(&same, 3), "fingerprint moved");
        assert_eq!(cache.get::<u32>("k"), Some(7), "stamps agree, cache kept");
    }
}
