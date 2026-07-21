//! Library handlers: flat paginated browse, iOS hierarchical navigation, album
//! covers (single + paginated), cover-cache status, radio stations, and
//! play-all. Response DTOs match the wire shapes, so handlers just serialize.

use std::collections::HashMap;

use serde::de::DeserializeOwned;
use serde::Serialize;
use serde_json::{json, Value};

use super::{as_bool_lenient, as_set_string, pagination, reply_dto, Ctx, HandlerResult};
use crate::cover::{cover_identifier, from_base64, store::CoverStore};
use crate::metadata_cache::MetadataCache;
use crate::protocol::messages::{AlbumCover, AlbumCoverItem, Page, Track};
use crate::providers::Providers;

// Metadata-cache keys for the small flat browse lists (genres/artists/albums).
// Shared by the handlers and the eager prewarm so the two can't drift. Tracks is
// NOT here: it is the one list large enough to OOM the 32-bit core as a blob, so
// it lives in the ordinal index + path-keyed tag cache (MBRCIP-0001), not the
// generic metadata cache.
// pub(crate) so the V6 library domain reuses the SAME cached blob lists the V4
// reconcile prewarms (no double storage).
pub(crate) const KEY_BROWSE_GENRES: &str = "browse_genres";
pub(crate) const KEY_BROWSE_ALBUMS: &str = "browse_albums";
pub(crate) fn key_browse_artists(album_artists: bool) -> String {
    format!("browse_artists:aa={album_artists}")
}

// ── Flat browse (paginated, served from the eager metadata cache) ──
//
// The metadata cache holds the FULL browse list; a request's page is sliced
// locally (matching the C# `Paginate` the host used to do per request). The
// full list is fetched from the provider with offset 0 / limit 0 (which returns
// everything), then cached when the cache is validated.

pub fn browse_genres(data: &Value, ctx: &Ctx) -> HandlerResult {
    let (offset, limit) = pagination(data);
    let page = flat_browse(ctx, KEY_BROWSE_GENRES, offset, limit, || {
        ctx.providers.browse_genres(0, 0)
    })?;
    reply_dto("browsegenres", &page)
}

pub fn browse_artists(data: &Value, ctx: &Ctx) -> HandlerResult {
    let (offset, limit) = pagination(data);
    let album_artists = data
        .get("album_artists")
        .and_then(Value::as_bool)
        .unwrap_or(false);
    let key = key_browse_artists(album_artists);
    let page = flat_browse(ctx, &key, offset, limit, || {
        ctx.providers.browse_artists(0, 0, album_artists)
    })?;
    reply_dto("browseartists", &page)
}

pub fn browse_albums(data: &Value, ctx: &Ctx) -> HandlerResult {
    let (offset, limit) = pagination(data);
    let page = flat_browse(ctx, KEY_BROWSE_ALBUMS, offset, limit, || {
        ctx.providers.browse_albums(0, 0)
    })?;
    reply_dto("browsealbums", &page)
}

pub fn browse_tracks(data: &Value, ctx: &Ctx) -> HandlerResult {
    let (offset, limit) = pagination(data);
    // Fast path: once the reconcile has built the ordinal index, serve the page
    // straight from the store - one redb range + one FFI batch for the page's
    // cache misses, never the whole library (MBRCIP-0001). Until the index exists
    // (store disabled, not yet validated, or pre-build window), fall back to
    // fetching the full list and slicing - correct, just O(library), and NOT
    // cached as a blob (that eager blob is exactly the 32-bit OOM we're removing).
    let page = match ctx.metadata_cache {
        Some(cache) if cache.track_count() > 0 => {
            serve_tracks_from_store(cache, ctx.providers, offset, limit)
        }
        _ => slice_page(ctx.providers.browse_tracks(0, 0)?, offset, limit),
    };
    reply_dto("browsetracks", &page)
}

/// Serve a browse-tracks page from the store: read the page's paths from the
/// ordinal index (O(page) range), resolve each path's `Track` from the path-keyed
/// tag cache, fill the misses with ONE `tracks_for_paths` FFI batch (cached for
/// next time), and assemble in index order. Nothing full-library ever loads.
fn serve_tracks_from_store(
    cache: &MetadataCache,
    p: &dyn Providers,
    offset: i32,
    limit: i32,
) -> Page<Track> {
    let total = cache.track_count() as i32;
    let paths = cache.track_page_paths(offset, limit);

    // Resolve cached tags; the `None` slots are this page's misses.
    let mut resolved: Vec<Option<Track>> = paths.iter().map(|p| cache.track_tags(p)).collect();
    let misses: Vec<String> = paths
        .iter()
        .zip(&resolved)
        .filter(|(_, tags)| tags.is_none())
        .map(|(path, _)| path.clone())
        .collect();

    if !misses.is_empty() {
        if let Ok(fetched) = p.tracks_for_paths(misses) {
            cache.put_track_tags(&fetched);
            // Fill the holes from the batch we just fetched (no second DB read).
            let mut by_path: HashMap<&str, &Track> =
                fetched.iter().map(|t| (t.src.as_str(), t)).collect();
            for (slot, path) in resolved.iter_mut().zip(&paths) {
                if slot.is_none() {
                    *slot = by_path.remove(path.as_str()).cloned();
                }
            }
        }
    }

    // A path still without tags (unreadable / not returned) degrades to a minimal
    // Track carrying just its src, so the page stays the right length and order.
    let data = paths
        .into_iter()
        .zip(resolved)
        .map(|(path, tags)| {
            tags.unwrap_or(Track {
                src: path,
                ..Default::default()
            })
        })
        .collect();

    Page {
        total,
        offset,
        limit,
        data,
    }
}

/// Eager-prewarm the small flat browse lists (genres/artists/albums) into the
/// metadata cache: fetch each full list from the provider once and cache it under
/// the handler's key, so the first client browse is already warm. These are
/// ~1-2 MB each (from `Library_QueryLookupTable`), so caching them whole is fine.
/// Tracks is deliberately excluded - it uses the ordinal index instead (see
/// [`build_track_index`]). Called by the reconcile once the cache is validated. A
/// provider error just leaves that list unwarmed (the handler lazily fills it on
/// first request). Returns the item count per list for the caller's log.
pub fn prewarm_browse_lists(
    cache: &MetadataCache,
    p: &dyn Providers,
) -> [(&'static str, usize); 4] {
    let mut counts = [
        ("genres", 0),
        ("artists", 0),
        ("albumartists", 0),
        ("albums", 0),
    ];
    if let Ok(page) = p.browse_genres(0, 0) {
        counts[0].1 = page.data.len();
        cache.put(KEY_BROWSE_GENRES, &page);
    }
    if let Ok(page) = p.browse_artists(0, 0, false) {
        counts[1].1 = page.data.len();
        cache.put(&key_browse_artists(false), &page);
    }
    if let Ok(page) = p.browse_artists(0, 0, true) {
        counts[2].1 = page.data.len();
        cache.put(&key_browse_artists(true), &page);
    }
    if let Ok(page) = p.browse_albums(0, 0) {
        counts[3].1 = page.data.len();
        cache.put(KEY_BROWSE_ALBUMS, &page);
    }
    counts
}

/// Build the ordinal track index from the provider's browse-ordered path list
/// (`Library_QueryFilesEx(null)` - one call, paths only, ~20 MB @ 200k), then
/// stamp the sync watermark to now so the first delta scan only picks up later
/// changes. NO eager tag prewarm: a page's tags are read lazily on first browse
/// and cached path-keyed. Bounds the reconcile's memory to the path list, never
/// the full-tag library. Returns the track count for the caller's log. A provider
/// error leaves the index empty (the handler falls back to a full fetch).
pub fn build_track_index(cache: &MetadataCache, p: &dyn Providers) -> usize {
    match p.track_paths() {
        Ok(paths) => {
            let count = paths.len();
            cache.replace_track_index(&paths);
            cache.set_tracks_synced_at(now_unix_seconds());
            count
        }
        Err(e) => {
            tracing::warn!(error = %e, "track index build: path fetch failed");
            0
        }
    }
}

/// Refresh the library caches incrementally (the Scanner's delta pass). Rebuilds
/// the ordinal index from the current path list (catches add / delete / reorder),
/// drops the cached tags of tracks the host reports changed since the watermark
/// (they re-read lazily), re-prewarms the small lists (an add / tag edit shifts
/// their counts), and advances the watermark. No-op until the cache is validated
/// (the init reconcile owns the first build). Best-effort: a failed provider call
/// leaves that piece stale for the next pass.
pub fn refresh_library_delta(cache: &MetadataCache, p: &dyn Providers) {
    if !cache.is_validated() {
        return;
    }
    let since = cache.tracks_synced_at();

    // Rebuild the ordinal index (browse order) - handles add / delete / reorder.
    match p.track_paths() {
        Ok(paths) => cache.replace_track_index(&paths),
        Err(e) => tracing::warn!(error = %e, "scanner: track path refetch failed"),
    }

    // Drop changed tracks' cached tags so the next serve re-reads them. `added`
    // are not cached yet (no-op); `updated` are the ones that matter; `deleted`
    // clears any orphaned tag rows the index rebuild left behind.
    match p.sync_delta(since) {
        Ok(delta) => {
            let mut changed = delta.updated;
            changed.extend(delta.added);
            changed.extend(delta.deleted);
            cache.drop_track_tags(&changed);
        }
        Err(e) => tracing::warn!(error = %e, "scanner: sync delta failed"),
    }

    // Small lists are cheap (~1-2 MB) and shift on add / tag edit; refresh whole.
    prewarm_browse_lists(cache, p);
    cache.set_tracks_synced_at(now_unix_seconds());
}

/// Current unix time in seconds (0 if the clock is before the epoch).
fn now_unix_seconds() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0)
}

/// The current track count (from the ordinal index), for the settings cache line.
/// Zero when the index has not been built.
pub fn cached_tracks_count(cache: &MetadataCache) -> usize {
    cache.track_count() as usize
}

/// Whether the browse cache is already warm from a previous run on the same
/// library. Keyed on the ordinal track index, which the reconcile builds last, so
/// its presence implies the small lists were prewarmed too. Lets the reconcile
/// skip the re-fetch when the library is unchanged and the persisted cache still
/// holds the index.
pub fn browse_lists_cached(cache: &MetadataCache) -> bool {
    cache.track_count() > 0
}

// ── Hierarchical navigation (iOS; name-keyed, non-paginated, lazily cached) ──

// The name key is coerced with `as_set_string` so an all-digit genre/artist/
// album name a client emits as a bare number (e.g. Adele "21", Taylor "1989")
// is still matched, exactly as the C# `GetDataOrDefault<string>()` did.
pub fn genre_artists(data: &Value, ctx: &Ctx) -> HandlerResult {
    let genre = as_set_string(data).unwrap_or_default();
    let list = nav_cached(ctx, &format!("genre_artists:{genre}"), || {
        ctx.providers.genre_artists(&genre)
    })?;
    reply_dto("librarygenreartists", &list)
}

pub fn artist_albums(data: &Value, ctx: &Ctx) -> HandlerResult {
    let artist = as_set_string(data).unwrap_or_default();
    let list = nav_cached(ctx, &format!("artist_albums:{artist}"), || {
        ctx.providers.artist_albums(&artist)
    })?;
    reply_dto("libraryartistalbums", &list)
}

pub fn album_tracks(data: &Value, ctx: &Ctx) -> HandlerResult {
    let album = as_set_string(data).unwrap_or_default();
    let list = nav_cached(ctx, &format!("album_tracks:{album}"), || {
        ctx.providers.album_tracks(&album)
    })?;
    reply_dto("libraryalbumtracks", &list)
}

// ── Metadata-cache helpers ──

/// Serve a flat browse list. On a cache hit the stored FULL list is sliced to
/// the requested page; on a miss (cache disabled, not yet validated, or first
/// fetch) `fetch_full` pulls the whole list from the provider once, caches it
/// (when validated), and slices. The slice mirrors C# `Paginate`.
fn flat_browse<T, F>(
    ctx: &Ctx,
    key: &str,
    offset: i32,
    limit: i32,
    fetch_full: F,
) -> Result<Page<T>, String>
where
    // `Default` is required because `Page<T>`'s derived `Deserialize` inherits it
    // from the `#[serde(default)]` on its `data` field; every browse item type
    // derives it.
    T: Serialize + DeserializeOwned + Default,
    F: FnOnce() -> Result<Page<T>, String>,
{
    if let Some(cache) = ctx.metadata_cache {
        if let Some(cached) = cache.get::<Page<T>>(key) {
            return Ok(slice_page(cached, offset, limit));
        }
    }
    let full = fetch_full()?;
    if let Some(cache) = ctx.metadata_cache {
        cache.put(key, &full);
    }
    Ok(slice_page(full, offset, limit))
}

/// Slice a full list into the requested page, byte-for-byte as C# `Paginate`:
/// `total` = the full list count, `data` = `skip(offset).take(limit)`, where a
/// non-positive `limit` means "the rest from offset".
fn slice_page<T>(full: Page<T>, offset: i32, limit: i32) -> Page<T> {
    let total = full.total;
    let start = (offset.max(0) as usize).min(full.data.len());
    let take = if limit > 0 {
        limit as usize
    } else {
        full.data.len()
    };
    let data = full.data.into_iter().skip(start).take(take).collect();
    Page {
        total,
        offset,
        limit,
        data,
    }
}

/// Serve a hierarchical (name-keyed) nav list, lazily cached on first request.
fn nav_cached<T, F>(ctx: &Ctx, key: &str, fetch: F) -> Result<Vec<T>, String>
where
    T: Serialize + DeserializeOwned,
    F: FnOnce() -> Result<Vec<T>, String>,
{
    if let Some(cache) = ctx.metadata_cache {
        if let Some(list) = cache.get::<Vec<T>>(key) {
            return Ok(list);
        }
    }
    let list = fetch()?;
    if let Some(cache) = ctx.metadata_cache {
        cache.put(key, &list);
    }
    Ok(list)
}

// ── Covers, radio, play-all ──

/// Paginated when `offset`/`limit` present, else a single cover by artist/album.
///
/// Covers are served from the core's `CoverStore` (resize/hash/cache all live in
/// the core now). When the store isn't wired - handler unit tests use a bare
/// `Ctx` - it falls back to the provider methods, so those tests are unchanged.
pub fn album_cover(data: &Value, ctx: &Ctx) -> HandlerResult {
    if data.get("offset").is_some() || data.get("limit").is_some() {
        let (offset, limit) = pagination(data);
        let page = match ctx.cover_store {
            Some(store) => store_cover_page(store, ctx.providers, offset, limit),
            None => ctx.providers.album_cover_page(offset, limit)?,
        };
        reply_dto("libraryalbumcover", &page)
    } else {
        let field = |key: &str| data.get(key).and_then(Value::as_str).unwrap_or("");
        let cover = match ctx.cover_store {
            Some(store) => store_single_cover(
                store,
                ctx.providers,
                field("artist"),
                field("album"),
                field("hash"),
            ),
            None => ctx
                .providers
                .album_cover(field("artist"), field("album"), field("hash"))?,
        };
        reply_dto("libraryalbumcover", &cover)
    }
}

pub fn cover_cache_status(ctx: &Ctx) -> HandlerResult {
    let building = match ctx.cover_store {
        Some(store) => store.is_building(),
        None => ctx.providers.cover_cache_status()?,
    };
    Ok(vec![(
        "librarycovercachebuildstatus".to_string(),
        json!(building),
    )])
}

/// A single album cover from the store (port of C# `CoverService.GetAlbumCover`).
/// On a cache miss, lazily fetches the album's raw artwork through the provider,
/// resizes + stores it, then serves. Empty album -> 400; no artwork -> 404.
fn store_single_cover(
    store: &CoverStore,
    p: &dyn Providers,
    artist: &str,
    album: &str,
    client_hash: &str,
) -> AlbumCover {
    if album.is_empty() {
        return status_cover(400);
    }
    let key = cover_identifier(artist, album);

    // Already cached: serve it (or 304 when the client's hash still matches).
    if let Some(hash) = store.hash_for(&key) {
        return serve_cover(store, client_hash, &hash);
    }

    // Miss: find the album's representative track and cache its cover now.
    let Some(path) = store.path_for(&key) else {
        return status_cover(404);
    };
    let Some(raw) = fetch_raw_artwork(p, &path) else {
        return status_cover(404);
    };
    match store.cache_cover(&key, &raw) {
        Ok(hash) => serve_cover(store, client_hash, &hash),
        Err(e) => {
            tracing::debug!(%path, error = %e, "album cover: cache failed");
            status_cover(404)
        }
    }
}

/// A page of album covers from the store (port of `CoverService.GetCoverPage`).
/// Resolves each cover's display artist/album via one batched host metadata call.
fn store_cover_page(
    store: &CoverStore,
    p: &dyn Providers,
    offset: i32,
    limit: i32,
) -> Page<AlbumCoverItem> {
    let keys = store.keys();
    let total = keys.len();
    let start = (offset.max(0) as usize).min(total);
    let take = if limit > 0 { limit as usize } else { total };
    let page_keys: Vec<String> = keys.into_iter().skip(start).take(take).collect();

    // One batched metadata call for every path on the page (not per cover).
    let paths: Vec<String> = page_keys.iter().filter_map(|k| store.path_for(k)).collect();
    let metadata = p.batch_metadata(paths).unwrap_or_default();
    let meta_for = |path: &str| {
        metadata
            .iter()
            .find(|m| m.path == path)
            .map(|m| (m.artist.clone(), m.album.clone()))
            .unwrap_or_default()
    };

    let data = page_keys
        .iter()
        .map(|key| {
            let path = store.path_for(key).unwrap_or_default();
            let (artist, album) = meta_for(&path);
            let cover = store
                .hash_for(key)
                .and_then(|hash| store.read_cover_base64(&hash))
                .unwrap_or_default();
            let hash = store.hash_for(key).unwrap_or_default();
            AlbumCoverItem {
                album,
                artist,
                status: if cover.is_empty() { 404 } else { 200 },
                cover,
                hash,
            }
        })
        .collect();

    Page {
        data,
        offset,
        limit,
        total: total as i32,
    }
}

/// Serve a cached cover by content hash, or a 304 when the client already holds
/// it (mirrors C# `GetAlbumCoverFromCache`).
fn serve_cover(store: &CoverStore, client_hash: &str, hash: &str) -> AlbumCover {
    if hash.is_empty() {
        return status_cover(404);
    }
    if !client_hash.is_empty() && client_hash == hash {
        return status_cover(304);
    }
    AlbumCover {
        cover: store.read_cover_base64(hash).unwrap_or_default(),
        status: 200,
        hash: hash.to_string(),
        ..Default::default()
    }
}

/// Fetch a track's raw artwork through the host (base64) and decode it. `None`
/// when there is no artwork or the payload is unusable.
fn fetch_raw_artwork(p: &dyn Providers, path: &str) -> Option<Vec<u8>> {
    let b64 = p.artwork_raw(path).ok()?;
    if b64.is_empty() {
        return None;
    }
    from_base64(&b64)
}

/// A status-only cover reply (`{status}`), for 304/400/404.
fn status_cover(status: i32) -> AlbumCover {
    AlbumCover {
        status,
        ..Default::default()
    }
}

pub fn radio_stations(data: &Value, p: &dyn Providers) -> HandlerResult {
    let (offset, limit) = pagination(data);
    reply_dto("radiostations", &p.radio_stations(offset, limit)?)
}

pub fn play_all(data: &Value, p: &dyn Providers) -> HandlerResult {
    p.play_all(as_bool_lenient(data).unwrap_or(false))?;
    Ok(vec![("libraryplayall".to_string(), json!(true))])
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::{ArtistData, Track};
    use crate::protocol::version::ProtocolVersion;
    use crate::providers::MockProviders;

    #[test]
    fn browse_artists_passes_album_artists_flag() {
        let m = MockProviders {
            browse_artists: Page {
                total: 3,
                ..Default::default()
            },
            ..Default::default()
        };
        let ctx = Ctx::new(&m, ProtocolVersion::V4);
        let out =
            browse_artists(&json!({"offset":0,"limit":100,"album_artists":true}), &ctx).unwrap();
        assert_eq!(out[0].0, "browseartists");
        assert_eq!(out[0].1["total"], json!(3));
        assert!(m.recorded().contains(&"browse_artists(true)".to_string()));
    }

    #[test]
    fn genre_artists_replies_bare_array() {
        let m = MockProviders {
            genre_artists: vec![ArtistData {
                artist: "A".into(),
                count: 2,
            }],
            ..Default::default()
        };
        let ctx = Ctx::new(&m, ProtocolVersion::V4);
        let out = genre_artists(&json!("Rock"), &ctx).unwrap();
        assert!(out[0].1.is_array());
        assert_eq!(out[0].1[0]["artist"], json!("A"));
        assert!(m.recorded().contains(&"genre_artists(Rock)".to_string()));
    }

    #[test]
    fn flat_browse_caches_full_list_and_slices_pages() {
        use crate::metadata_cache::MetadataCache;
        use crate::protocol::messages::AlbumData;
        use crate::store::Db;

        let dir = std::env::temp_dir().join("mbrc-meta-browse-slice");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let cache = MetadataCache::new(Db::open(dir.to_str().unwrap()));
        cache.reconcile(1); // validate so the cache is live

        let album = |name: &str| AlbumData {
            album: name.into(),
            artist: "x".into(),
            count: 1,
        };
        let m = MockProviders {
            browse_albums: Page {
                total: 3,
                offset: 0,
                limit: 0,
                data: vec![album("a"), album("b"), album("c")],
            },
            ..Default::default()
        };
        let ctx = Ctx::new(&m, ProtocolVersion::V4).with_metadata_cache(&cache);
        let calls = || {
            m.recorded()
                .iter()
                .filter(|c| c.as_str() == "browse_albums")
                .count()
        };

        // First request misses: the full list is fetched once and sliced.
        let out = browse_albums(&json!({"offset":1,"limit":1}), &ctx).unwrap();
        assert_eq!(out[0].1["total"], json!(3), "total is the full count");
        assert_eq!(out[0].1["data"].as_array().unwrap().len(), 1);
        assert_eq!(out[0].1["data"][0]["album"], json!("b"), "sliced page");
        assert_eq!(calls(), 1);

        // A different page is served from the cache - no second provider call.
        let out2 = browse_albums(&json!({"offset":0,"limit":2}), &ctx).unwrap();
        assert_eq!(out2[0].1["data"].as_array().unwrap().len(), 2);
        assert_eq!(out2[0].1["data"][0]["album"], json!("a"));
        assert_eq!(calls(), 1, "cache hit: provider not called again");
    }

    #[test]
    fn browse_tracks_serves_from_store_with_one_batch_then_caches() {
        use crate::metadata_cache::MetadataCache;
        use crate::store::Db;

        let dir = std::env::temp_dir().join("mbrc-browse-tracks-store");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let cache = MetadataCache::new(Db::open(dir.to_str().unwrap()));
        cache.reconcile(1); // validate so the store is live

        // Build a 5-track ordinal index (browse order).
        let paths: Vec<String> = (0..5).map(|i| format!("/m/{i}.mp3")).collect();
        cache.replace_track_index(&paths);

        let m = MockProviders {
            tracks_for_paths: (0..5)
                .map(|i| Track {
                    src: format!("/m/{i}.mp3"),
                    title: format!("t{i}"),
                    ..Default::default()
                })
                .collect(),
            ..Default::default()
        };
        let ctx = Ctx::new(&m, ProtocolVersion::V4).with_metadata_cache(&cache);
        let batch_calls = || {
            m.recorded()
                .iter()
                .filter(|c| c.starts_with("tracks_for_paths("))
                .count()
        };

        // First page (offset 1, limit 2): total is the full index; data is the
        // sliced page in index order; misses filled by ONE FFI batch.
        let out = browse_tracks(&json!({"offset":1,"limit":2}), &ctx).unwrap();
        assert_eq!(out[0].1["total"], json!(5), "total is the index length");
        let data = out[0].1["data"].as_array().unwrap();
        assert_eq!(data.len(), 2);
        assert_eq!(data[0]["src"], json!("/m/1.mp3"));
        assert_eq!(data[0]["title"], json!("t1"), "tags came from the batch");
        assert_eq!(data[1]["src"], json!("/m/2.mp3"));
        assert_eq!(batch_calls(), 1);

        // The same page again: all cached now - no second FFI batch.
        let _ = browse_tracks(&json!({"offset":1,"limit":2}), &ctx).unwrap();
        assert_eq!(batch_calls(), 1, "cache hit: provider not called again");

        // The full browse-tracks provider is never used on the store path.
        assert!(!m.recorded().iter().any(|c| c == "browse_tracks"));
    }

    #[test]
    fn build_track_index_populates_from_provider_paths_and_stamps_watermark() {
        use crate::metadata_cache::MetadataCache;
        use crate::store::Db;

        let dir = std::env::temp_dir().join("mbrc-build-track-index");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let cache = MetadataCache::new(Db::open(dir.to_str().unwrap()));
        cache.reconcile(1);

        let m = MockProviders {
            track_paths: vec!["/a.mp3".into(), "/b.mp3".into(), "/c.mp3".into()],
            ..Default::default()
        };
        let n = build_track_index(&cache, &m);

        assert_eq!(n, 3);
        assert_eq!(cache.track_count(), 3);
        assert_eq!(
            cache.track_page_paths(0, 2),
            vec!["/a.mp3".to_string(), "/b.mp3".to_string()],
            "index is in the provider's browse order"
        );
        assert!(cache.tracks_synced_at() > 0, "watermark stamped to now");
        assert!(m.recorded().contains(&"track_paths".to_string()));
        // The full-tag browse provider is never called - no eager tag prewarm.
        assert!(!m.recorded().iter().any(|c| c == "browse_tracks"));
        // Settings count + warm check now read the index.
        assert_eq!(cached_tracks_count(&cache), 3);
        assert!(browse_lists_cached(&cache));
    }

    #[test]
    fn refresh_library_delta_rebuilds_index_and_drops_changed_tags() {
        use crate::metadata_cache::MetadataCache;
        use crate::protocol::messages::SyncDelta;
        use crate::store::Db;

        let dir = std::env::temp_dir().join("mbrc-refresh-delta");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let cache = MetadataCache::new(Db::open(dir.to_str().unwrap()));
        cache.reconcile(1);

        // Seed an index + cached tags for two tracks, at watermark 100.
        cache.replace_track_index(&["/a.mp3".into(), "/b.mp3".into()]);
        cache.put_track_tags(&[
            Track {
                src: "/a.mp3".into(),
                title: "A".into(),
                ..Default::default()
            },
            Track {
                src: "/b.mp3".into(),
                title: "B".into(),
                ..Default::default()
            },
        ]);
        cache.set_tracks_synced_at(100);

        // Provider: a file was added (index now 3), and /a.mp3 was edited.
        let m = MockProviders {
            track_paths: vec!["/a.mp3".into(), "/b.mp3".into(), "/c.mp3".into()],
            sync_delta: SyncDelta {
                updated: vec!["/a.mp3".into()],
                ..Default::default()
            },
            ..Default::default()
        };

        refresh_library_delta(&cache, &m);

        // Index rebuilt to the new path list.
        assert_eq!(cache.track_count(), 3);
        // The edited track's cached tags are dropped (re-read lazily); the
        // untouched track's tags survive.
        assert!(
            cache.track_tags("/a.mp3").is_none(),
            "changed track's tags dropped"
        );
        assert_eq!(
            cache.track_tags("/b.mp3").unwrap().title,
            "B",
            "unchanged track's tags kept"
        );
        // Watermark advanced past the old value; delta queried with the OLD one.
        assert!(cache.tracks_synced_at() > 100);
        assert!(m.recorded().contains(&"sync_delta(100)".to_string()));
        assert!(m.recorded().contains(&"track_paths".to_string()));
    }

    #[test]
    fn browse_tracks_falls_back_to_provider_without_index() {
        // No metadata cache wired (bare Ctx): the store fast path is skipped and
        // the handler fetches the full list and slices it.
        let m = MockProviders {
            browse_tracks: Page {
                total: 2,
                data: vec![
                    Track {
                        src: "/a.mp3".into(),
                        ..Default::default()
                    },
                    Track {
                        src: "/b.mp3".into(),
                        ..Default::default()
                    },
                ],
                ..Default::default()
            },
            ..Default::default()
        };
        let ctx = Ctx::new(&m, ProtocolVersion::V4);
        let out = browse_tracks(&json!({"offset":0,"limit":1}), &ctx).unwrap();
        assert_eq!(out[0].1["total"], json!(2));
        assert_eq!(out[0].1["data"].as_array().unwrap().len(), 1);
        assert!(m.recorded().contains(&"browse_tracks".to_string()));
    }

    #[test]
    fn album_tracks_omits_album_and_genre() {
        let m = MockProviders {
            album_tracks: vec![Track {
                src: "s".into(),
                artist: "ar".into(),
                title: "t".into(),
                trackno: 1,
                disc: 1,
                album_artist: "aa".into(),
                ..Default::default() // album + genre empty
            }],
            ..Default::default()
        };
        let ctx = Ctx::new(&m, ProtocolVersion::V4);
        let out = album_tracks(&json!("Album"), &ctx).unwrap();
        let item = &out[0].1[0];
        assert_eq!(item["album_artist"], json!("aa"));
        assert!(item.get("album").is_none());
        assert!(item.get("genre").is_none());
    }

    // With no cover store wired (bare Ctx), the handlers delegate to the
    // provider - the path handler unit tests take.
    #[test]
    fn album_cover_falls_back_to_provider_without_store() {
        let m = MockProviders {
            album_cover: AlbumCover {
                status: 404,
                ..Default::default()
            },
            ..Default::default()
        };
        let ctx = Ctx::new(&m, ProtocolVersion::V4);
        // single: status-only shape
        let single = album_cover(&json!({"artist":"a","album":"b"}), &ctx).unwrap();
        assert_eq!(single[0].1, json!({"status": 404}));
        assert!(m.recorded().iter().any(|c| c.starts_with("album_cover(")));
        // paginated
        let paged = album_cover(&json!({"offset":0,"limit":20}), &ctx).unwrap();
        assert!(paged[0].1.get("data").is_some());
        assert!(m.recorded().contains(&"album_cover_page".to_string()));
    }

    #[test]
    fn cover_cache_status_falls_back_to_provider_without_store() {
        let m = MockProviders {
            cover_cache_status: true,
            ..Default::default()
        };
        let ctx = Ctx::new(&m, ProtocolVersion::V4);
        assert_eq!(
            cover_cache_status(&ctx).unwrap()[0],
            ("librarycovercachebuildstatus".into(), json!(true))
        );
    }

    #[test]
    fn play_all_delegates() {
        let m = MockProviders::default();
        assert_eq!(
            play_all(&json!(true), &m).unwrap()[0],
            ("libraryplayall".into(), json!(true))
        );
        assert!(m.recorded().contains(&"play_all(true)".to_string()));
    }

    // ── Store-backed cover serving (the production path) ──

    use crate::cover::test_jpeg_bytes;
    use std::path::PathBuf;

    fn temp_store(name: &str) -> (CoverStore, PathBuf) {
        let dir = std::env::temp_dir().join(format!("mbrc-cover-handler-{name}"));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        (CoverStore::open_at(&dir), dir)
    }

    #[test]
    fn single_cover_lazily_caches_then_304s_on_matching_hash() {
        use crate::cover::store::AlbumIdentity;
        let (store, _dir) = temp_store("single");
        let key = cover_identifier("Artist", "Album");
        store.warm_up(&[AlbumIdentity {
            key: key.clone(),
            path: "/a.mp3".into(),
            modified: 0,
        }]);
        // The host returns this track's raw artwork as base64.
        let m = MockProviders {
            artwork_raw: crate::cover::to_base64(&test_jpeg_bytes(300, 300)),
            ..Default::default()
        };

        // First request misses the cache, lazily fetches + stores, serves 200.
        let served = store_single_cover(&store, &m, "Artist", "Album", "");
        assert_eq!(served.status, 200);
        assert!(!served.cover.is_empty());
        assert_eq!(served.hash.len(), 40);
        assert!(m.recorded().iter().any(|c| c.starts_with("artwork_raw(")));

        // Re-request with the same hash -> 304 (no bytes re-sent).
        let revalidated = store_single_cover(&store, &m, "Artist", "Album", &served.hash);
        assert_eq!(revalidated.status, 304);
        assert!(revalidated.cover.is_empty());
    }

    #[test]
    fn single_cover_status_codes() {
        let (store, _dir) = temp_store("codes");
        let m = MockProviders::default();
        // Empty album -> 400.
        assert_eq!(store_single_cover(&store, &m, "A", "", "").status, 400);
        // Unknown album (no warm-up entry) -> 404.
        assert_eq!(store_single_cover(&store, &m, "A", "Nope", "").status, 404);
    }

    #[test]
    fn cover_page_resolves_metadata_and_paginates() {
        use crate::cover::store::AlbumIdentity;
        use crate::protocol::messages::TrackMetadata;
        let (store, _dir) = temp_store("page");
        let key = cover_identifier("Artist", "Album");
        store.warm_up(&[AlbumIdentity {
            key: key.clone(),
            path: "/a.mp3".into(),
            modified: 0,
        }]);
        store.cache_cover(&key, &test_jpeg_bytes(200, 200)).unwrap();

        let m = MockProviders {
            batch_metadata: vec![TrackMetadata {
                path: "/a.mp3".into(),
                artist: "Artist".into(),
                album: "Album".into(),
            }],
            ..Default::default()
        };
        let page = store_cover_page(&store, &m, 0, 20);
        assert_eq!(page.total, 1);
        assert_eq!(page.data.len(), 1);
        assert_eq!(page.data[0].artist, "Artist");
        assert_eq!(page.data[0].album, "Album");
        assert_eq!(page.data[0].status, 200);
        assert!(!page.data[0].cover.is_empty());
    }

    #[test]
    fn cover_cache_status_reads_store_building_flag() {
        let (store, _dir) = temp_store("status");
        let m = MockProviders::default();
        let ctx = Ctx::new(&m, ProtocolVersion::V4).with_cover_store(&store);
        // A freshly created store is not building.
        assert_eq!(cover_cache_status(&ctx).unwrap()[0].1, json!(false));
    }
}
