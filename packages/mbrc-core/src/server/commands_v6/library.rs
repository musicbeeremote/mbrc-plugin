//! V6 library domain: browse the library as paginated lists.
//!
//! Unifies V4's separate flat-browse and hierarchical-nav ops into one op per
//! level with an optional filter (#118's "one canonical list + parameter"):
//!
//! - `library_artists { genre? }` - all artists, or a genre's artists
//! - `library_albums { artist? }` - all albums, or an artist's albums
//! - `library_tracks { album?, artist? }` - all tracks, or one album's
//!
//! Each also takes `query`, a substring filter over the names that level shows,
//! and `sort`/`order`. A filter is a parameter rather than an op of its own so
//! that searching and sorting compose with browsing instead of duplicating it.
//!
//! Plus `library_genres`, `library_radio`, `library_play_all`, and
//! `library_queue { genre?, artist?, album?, query?, mode, play?, shuffle? }`,
//! which queues what those same filters would list. It exists for a client that
//! holds only a page of the library and so cannot name the paths itself.
//!
//! Every list is a V6 `Page` (`{ total, offset, items }`).

use std::collections::BTreeMap;

use serde::Serialize;
use serde::de::DeserializeOwned;
use serde_json::{Value, json};

use mbrc_wire::v6::ErrorCode;

use super::{OpResult, V6Error, internal, opt_bool, opt_str, page_args, page_json, track};
use crate::cover::store::CoverStore;
use crate::metadata_cache::{
    CachedTags, MetadataCache, SortField, TrackFilter, album_key, collate, relevance,
};
use crate::protocol::messages::{AlbumData, ArtistData, Page, Track, TrackTags};
use crate::providers::Providers;
use crate::server::commands::library::{KEY_BROWSE_ALBUMS, KEY_BROWSE_GENRES, key_browse_artists};

/// The op names this domain serves (advertised in the handshake capabilities).
pub const OPS: &[&str] = &[
    "library_genres",
    "library_artists",
    "library_albums",
    "library_tracks",
    "library_radio",
    "library_play_all",
    "library_queue",
];

/// Dispatch a `library_*` op. `None` if `op` is not in this domain.
pub fn dispatch(
    op: &str,
    data: &Value,
    p: &dyn Providers,
    cover_store: Option<&CoverStore>,
    metadata_cache: Option<&MetadataCache>,
) -> Option<OpResult> {
    Some(match op {
        "library_genres" => genres(data, p, metadata_cache),
        "library_artists" => artists(data, p, metadata_cache),
        "library_albums" => albums(data, p, cover_store, metadata_cache),
        "library_tracks" => tracks(data, p, cover_store, metadata_cache),
        "library_radio" => radio(data, p),
        "library_play_all" => play_all(data, p),
        "library_queue" => queue(data, p, metadata_cache),
        _ => return None,
    })
}

fn genres(data: &Value, p: &dyn Providers, cache: Option<&MetadataCache>) -> OpResult {
    let (offset, limit) = page_args(data)?;
    let mut all = flat_list(cache, KEY_BROWSE_GENRES, || p.browse_genres(0, 0))?;
    let needle = needle(data)?;
    if let Some(n) = &needle {
        all.retain(|g| contains(&g.genre, n));
    }
    sort_by_name(data, &mut all, |g| &g.genre)?;
    if let Some(n) = &needle {
        rank_by_relevance(&mut all, n, |g| &g.genre);
    }
    let total = all.len();
    let items = slice(all, offset, limit)
        .into_iter()
        .map(|g| json!({ "genre": g.genre, "count": g.count }))
        .collect();
    Ok(page_json(total, offset, items))
}

fn artists(data: &Value, p: &dyn Providers, cache: Option<&MetadataCache>) -> OpResult {
    let (offset, limit) = page_args(data)?;
    // With a `genre` filter, navigate that genre's artists; otherwise the flat list.
    let mut all: Vec<ArtistData> = match opt_str(data, "genre")? {
        Some(genre) => p.genre_artists(genre).map_err(internal)?,
        None => {
            let album_artists = opt_bool(data, "album_artists")?.unwrap_or(false);
            flat_list(cache, &key_browse_artists(album_artists), || {
                p.browse_artists(0, 0, album_artists)
            })?
        }
    };
    let needle = needle(data)?;
    if let Some(n) = &needle {
        all.retain(|a| contains(&a.artist, n));
    }
    sort_by_name(data, &mut all, |a| &a.artist)?;
    if let Some(n) = &needle {
        rank_by_relevance(&mut all, n, |a| &a.artist);
    }
    let total = all.len();
    let items = slice(all, offset, limit)
        .into_iter()
        .map(|a| json!({ "artist": a.artist, "count": a.count }))
        .collect();
    Ok(page_json(total, offset, items))
}

fn albums(
    data: &Value,
    p: &dyn Providers,
    store: Option<&CoverStore>,
    cache: Option<&MetadataCache>,
) -> OpResult {
    let (offset, limit) = page_args(data)?;
    let all: Vec<AlbumData> = match opt_str(data, "artist")? {
        Some(artist) => match p.artist_albums(artist).map_err(internal)? {
            found if found.is_empty() => albums_the_tags_name(cache, artist),
            found => found,
        },
        None => flat_list(cache, KEY_BROWSE_ALBUMS, || p.browse_albums(0, 0))?,
    };

    // Derived from the tracks' own tags, since the album lookup carries no year.
    // Zero means undated, which sorts before every real year.
    let years = cache.map(MetadataCache::album_years).unwrap_or_default();
    let mut all: Vec<(AlbumData, i32)> = all
        .into_iter()
        .map(|al| {
            let year = years
                .get(&album_key(&al.artist, &al.artist, &al.album))
                .copied()
                .unwrap_or(0);
            (al, year)
        })
        .collect();

    // The artist is shown beside the album, so it is part of what a search of
    // this level searches, and of what makes a hit a good one.
    let needle = needle(data)?;
    if let Some(n) = &needle {
        all.retain(|(al, _)| contains(&al.album, n) || contains(&al.artist, n));
    }
    match opt_str(data, "sort")? {
        None => {}
        Some("name") => sort_names(&mut all, descending(data)?, |(al, _)| &al.album),
        Some("artist") => sort_names(&mut all, descending(data)?, |(al, _)| &al.artist),
        Some("year") => {
            all.sort_by_key(|(al, year)| (*year, collate(&al.album)));
            if descending(data)? {
                all.reverse();
            }
        }
        Some(other) => {
            return Err(V6Error::field(
                ErrorCode::InvalidField,
                "sort",
                format!("unknown sort field: {other}"),
            ));
        }
    }
    if let Some(n) = &needle {
        all.sort_by_key(|(al, _)| relevance(&al.album, n).min(relevance(&al.artist, n)));
    }
    let total = all.len();
    let items = slice(all, offset, limit)
        .into_iter()
        .map(|(al, year)| {
            let mut obj = json!({ "album": al.album, "artist": al.artist, "count": al.count });
            if let Some(hash) = track::album_cover_hash(store, &al.artist, &al.album) {
                obj["cover_hash"] = json!(hash);
            }
            if year > 0 {
                obj["year"] = json!(year);
            }
            obj
        })
        .collect();
    Ok(page_json(total, offset, items))
}

/// One named album's tracks, ordered and sliced to the page.
///
/// The artist chooses which record is meant when several share a title, by the
/// same rule the queue uses, so a list and what it queues agree. The walk is
/// already in hand, so an order here is a sort rather than an index read.
#[allow(clippy::too_many_arguments)]
fn album_page(
    p: &dyn Providers,
    album: &str,
    artist: Option<&str>,
    needle: &Option<String>,
    sort: Option<SortField>,
    descending: bool,
    offset: i64,
    limit: i64,
) -> Result<(usize, Vec<String>), V6Error> {
    let walk = p.album_tracks(album).map_err(internal)?;
    let chooses = chooses_between_albums(&walk, artist);
    let mut tracks: Vec<CachedTags> = one_album(walk.iter(), chooses)
        .into_iter()
        .filter(|t| match needle {
            Some(n) => contains(&t.title, n) || contains(&t.artist, n),
            None => true,
        })
        .map(CachedTags::from)
        .collect();
    if let Some(field) = sort {
        tracks.sort_by(|a, b| field.compare(a, b));
        if descending {
            tracks.reverse();
        }
    }
    let paths: Vec<String> = tracks.into_iter().map(|t| t.src).collect();
    let total = paths.len();
    Ok((total, slice(paths, offset, limit)))
}

fn tracks(
    data: &Value,
    p: &dyn Providers,
    store: Option<&CoverStore>,
    cache: Option<&MetadataCache>,
) -> OpResult {
    let (offset, limit) = page_args(data)?;
    let needle = needle(data)?;
    let sort = sort_field(data)?;
    let descending = descending(data)?;
    // Resolve the scope's ordered paths, sliced to the page.
    let (total, page_paths): (usize, Vec<String>) = match opt_str(data, "album")? {
        Some(album) => album_page(
            p,
            album,
            opt_str(data, "artist")?,
            &needle,
            sort,
            descending,
            offset,
            limit,
        )?,
        None => unalbumed_page(data, p, cache, &needle, sort, descending, offset, limit)?,
    };
    // One batch read for the page's typed tags -> canonical tracks.
    let tags = p.tracks_detailed_for_paths(page_paths).map_err(internal)?;
    cache_browse_tags(cache, &tags);
    let items = tags
        .iter()
        .map(|t| track::track_json(t, track::cover_hash_for(store, t).as_deref()))
        .collect();
    Ok(page_json(total, offset, items))
}

/// The tracks of a level that named no album, sliced to the page.
///
/// A query is answered from tag content, which the ordinal index says nothing
/// about, so this is the one browse path that scans. An artist or a genre
/// narrows the same way: naming one and being handed the whole library is a
/// wrong answer rather than a missing filter. Naming none of them is the
/// library itself, which the ordinal index pages without reading a tag.
#[expect(
    clippy::too_many_arguments,
    reason = "the page and the scope it slices"
)]
fn unalbumed_page(
    data: &Value,
    p: &dyn Providers,
    cache: Option<&MetadataCache>,
    needle: &Option<String>,
    sort: Option<SortField>,
    descending: bool,
    offset: i64,
    limit: i64,
) -> Result<(usize, Vec<String>), V6Error> {
    let scope = TrackFilter {
        query: needle.as_deref(),
        artist: opt_str(data, "artist")?,
        album: None,
        genre: opt_str(data, "genre")?,
    };
    let narrowed = scope.query.is_some() || scope.artist.is_some() || scope.genre.is_some();

    Ok(match (narrowed, cache) {
        (true, Some(c)) if c.track_count() > 0 => {
            let paths = c.track_paths_where(&scope, sort, descending);
            let total = paths.len();
            (total, slice(paths, offset, limit))
        }
        (false, Some(c)) if sort.is_some() && sorted_ready(c, sort) => {
            let field = sort.unwrap_or(SortField::Title);
            (
                c.track_count() as usize,
                c.sorted_track_page(field, offset as i32, limit as i32, descending),
            )
        }
        (false, Some(c)) if c.track_count() > 0 => (
            c.track_count() as usize,
            c.track_page_paths(offset as i32, limit as i32),
        ),
        // Cold cache: the path list, filtered on the path itself since no tags
        // are cached to match against yet.
        _ => {
            let mut paths = p.track_paths().map_err(internal)?;
            if let Some(n) = needle {
                paths.retain(|path| contains(path, n));
            }
            let total = paths.len();
            (total, slice(paths, offset, limit))
        }
    })
}

/// The albums an artist's own tracks name, for an artist the host reports none
/// for.
///
/// A track tagged with no artist and no album belongs to a group MusicBee's
/// album enumeration does not report, which leaves the artist in the list and
/// impossible to open: every album is somewhere, so an artist with tracks and
/// no albums is a hole rather than an answer. The cached tags say what the host
/// will not.
fn albums_the_tags_name(cache: Option<&MetadataCache>, artist: &str) -> Vec<AlbumData> {
    let Some(cache) = cache else {
        return Vec::new();
    };
    let filter = TrackFilter {
        artist: Some(artist),
        ..Default::default()
    };

    let mut counts: BTreeMap<String, i32> = BTreeMap::new();
    for path in cache.track_paths_where(&filter, None, false) {
        if let Some(tags) = cache.track_tags(&path) {
            *counts.entry(tags.album).or_default() += 1;
        }
    }

    counts
        .into_iter()
        .map(|(album, count)| AlbumData {
            album,
            artist: artist.to_string(),
            count,
        })
        .collect()
}

fn radio(data: &Value, p: &dyn Providers) -> OpResult {
    let (offset, limit) = page_args(data)?;
    // Radio is provider-paginated (no cache), so pass the window straight through.
    let page = p
        .radio_stations(offset as i32, limit as i32)
        .map_err(internal)?;
    let total = page.total.max(0) as usize;
    let items = page
        .data
        .into_iter()
        .map(|r| json!({ "name": r.name, "url": r.url }))
        .collect();
    Ok(page_json(total, offset, items))
}

/// Queues every track the given filters select, in browse order.
///
/// Replies the number queued rather than the paths: the caller named a scope
/// precisely so it would not have to handle the list.
fn queue(data: &Value, p: &dyn Providers, cache: Option<&MetadataCache>) -> OpResult {
    let mode = super::nowplaying_list::queue_mode(data)?;
    let play = opt_str(data, "play")?.unwrap_or("");
    let mut paths = scope_paths(data, p, cache)?;
    if opt_bool(data, "shuffle")?.unwrap_or(false) {
        shuffle(&mut paths);
    }
    let count = paths.len();
    if count > 0 {
        p.queue(mode, paths, play).map_err(internal)?;
    }
    Ok(json!({ "count": count }))
}

/// Shuffles the selection in place, Fisher-Yates.
///
/// Shuffling what was selected, rather than turning the player's shuffle mode
/// on: "shuffle this artist" is a statement about the order these tracks go in,
/// not a request to change a setting that outlives them.
fn shuffle(paths: &mut [String]) {
    if paths.len() < 2 {
        return;
    }
    let mut bytes = vec![0u8; paths.len() * 4];
    if getrandom::fill(&mut bytes).is_err() {
        return;
    }
    for i in (1..paths.len()).rev() {
        let chunk = &bytes[i * 4..i * 4 + 4];
        let r = u32::from_le_bytes([chunk[0], chunk[1], chunk[2], chunk[3]]) as usize;
        paths.swap(i, r % (i + 1));
    }
}

/// The paths a scope selects, in browse order.
///
/// A named artist, album or genre goes through the provider even when the tag
/// index is warm: the index is filled by browsing, so an artist resolved from
/// it would queue only the albums already visited and call that the artist.
/// Only an unscoped search reads the index, which answers it in one pass.
fn scope_paths(
    data: &Value,
    p: &dyn Providers,
    cache: Option<&MetadataCache>,
) -> Result<Vec<String>, V6Error> {
    let needle = needle(data)?;
    let filter = TrackFilter {
        query: needle.as_deref(),
        artist: opt_str(data, "artist")?,
        album: opt_str(data, "album")?,
        genre: opt_str(data, "genre")?,
    };

    if filter.artist.is_some() || filter.album.is_some() || filter.genre.is_some() {
        return cold_scope_paths(&filter, p);
    }
    if let Some(c) = cache
        && c.track_count() > 0
    {
        return Ok(c.track_paths_where(&filter, None, false));
    }
    cold_scope_paths(&filter, p)
}

/// A scope resolved through the provider's own navigation, narrowest first.
///
/// A genre is asked of the library directly rather than walked artist by
/// artist: `album_tracks` leaves the genre field empty, so a walk cannot tell
/// an artist's tracks in this genre from their tracks in another.
fn cold_scope_paths(filter: &TrackFilter<'_>, p: &dyn Providers) -> Result<Vec<String>, V6Error> {
    if let Some(album) = filter.album {
        let tracks = p.album_tracks(album).map_err(internal)?;
        return Ok(album_scope_paths(filter, tracks));
    }

    let albums: Vec<String> = match (filter.album, filter.artist, filter.genre) {
        (Some(album), _, _) => vec![album.to_string()],
        (None, Some(artist), _) => p
            .artist_albums(artist)
            .map_err(internal)?
            .into_iter()
            .map(|a| a.album)
            .collect(),
        (None, None, Some(genre)) => {
            let tracks = p.genre_tracks(genre).map_err(internal)?;
            let by_query = TrackFilter {
                query: filter.query,
                artist: None,
                album: None,
                genre: None,
            };
            return Ok(tracks
                .into_iter()
                .filter(|t| admits(&by_query, t))
                .map(|t| t.src)
                .collect());
        }
        // No scope at all: the whole library, which the provider lists directly.
        (None, None, None) => {
            let mut paths = p.track_paths().map_err(internal)?;
            if let Some(q) = filter.query {
                paths.retain(|path| contains(path, q));
            }
            return Ok(paths);
        }
    };

    let mut paths = Vec::new();
    for album in albums {
        for track in p.album_tracks(&album).map_err(internal)? {
            if admits(filter, &track) {
                paths.push(track.src);
            }
        }
    }
    Ok(paths)
}

/// The artist that chooses between the albums an album walk returned.
///
/// A title is shared often enough that a walk can hold several records; the
/// artist says which is meant. When it files none of them the walk is one album
/// and the artist is merely where the reader came from - a record credited to
/// "A & B", or to "Various Artists", is still that record when reached through
/// A, so it must not be trimmed to the tracks bearing A's name.
fn chooses_between_albums<'a>(tracks: &[Track], artist: Option<&'a str>) -> Option<&'a str> {
    let artist = artist?;
    tracks
        .iter()
        .any(|t| t.album_artist.eq_ignore_ascii_case(artist))
        .then_some(artist)
}

/// Narrows an album walk to the one record the scope means.
fn one_album<'a>(
    tracks: impl IntoIterator<Item = &'a Track>,
    artist: Option<&str>,
) -> Vec<&'a Track> {
    tracks
        .into_iter()
        .filter(|t| artist.is_none_or(|a| t.album_artist.eq_ignore_ascii_case(a)))
        .collect()
}

/// The tracks of one named album.
fn album_scope_paths(filter: &TrackFilter<'_>, tracks: Vec<Track>) -> Vec<String> {
    let chooses = chooses_between_albums(&tracks, filter.artist);
    one_album(tracks.iter(), chooses)
        .into_iter()
        .filter(|t| {
            filter
                .query
                .is_none_or(|q| contains(&t.title, q) || contains(&t.artist, q))
        })
        .map(|t| t.src.clone())
        .collect()
}

/// Whether a track from an album walk belongs to the scope.
///
/// The genre is not among the tests. Tracks reach here from `album_tracks`,
/// which reports no genre, so comparing one rejects everything - and a genre
/// only reaches here alongside an artist or an album, which the reader named and
/// which already says more than the genre they browsed through did.
fn admits(filter: &TrackFilter<'_>, track: &Track) -> bool {
    // Either credit, since which one names the artist list depends on the
    // album-artists setting.
    filter.artist.is_none_or(|a| {
        track.artist.eq_ignore_ascii_case(a) || track.album_artist.eq_ignore_ascii_case(a)
    }) && filter
        .query
        .is_none_or(|q| contains(&track.title, q) || contains(&track.artist, q))
}

fn play_all(data: &Value, p: &dyn Providers) -> OpResult {
    let shuffle = opt_bool(data, "shuffle")?.unwrap_or(false);
    p.play_all(shuffle).map_err(internal)?;
    Ok(json!({}))
}

/// Files a browsed page's tags into the cache the way the V4 handler does.
///
/// Browsing is the only thing that fills the tag cache, and until it is filled a
/// track search has nothing but file paths to match on. Writing here means
/// searching gets better as the library is browsed rather than staying blind
/// wherever V4 has never been.
fn cache_browse_tags(cache: Option<&MetadataCache>, tags: &[TrackTags]) {
    let Some(cache) = cache else { return };
    let cached: Vec<CachedTags> = tags.iter().map(CachedTags::from).collect();
    cache.put_track_tags(&cached);
}

/// Applies the one order a name list has, when asked for.
fn sort_by_name<T>(
    data: &Value,
    all: &mut [T],
    name: impl Fn(&T) -> &str + Copy,
) -> Result<(), V6Error> {
    match opt_str(data, "sort")? {
        None => Ok(()),
        Some("name") => {
            sort_names(all, descending(data)?, name);
            Ok(())
        }
        Some(other) => Err(V6Error::field(
            ErrorCode::InvalidField,
            "sort",
            format!("unknown sort field: {other}"),
        )),
    }
}

/// Sorts by a name the way a reader files it: case-folded, article dropped.
fn sort_names<T>(all: &mut [T], descending: bool, name: impl Fn(&T) -> &str + Copy) {
    all.sort_by_key(|entry| collate(name(entry)));
    if descending {
        all.reverse();
    }
}

/// Whether a sort order is built and covers the whole library.
///
/// A half-built order would page a client through a fraction of the library and
/// call it the whole thing, so browse order is the honest answer until it is
/// complete.
fn sorted_ready(cache: &MetadataCache, sort: Option<SortField>) -> bool {
    let Some(field) = sort else { return false };
    let total = cache.track_count();
    total > 0 && cache.sorted_track_count(field) == total
}

/// The `sort` parameter, or nothing when the caller did not ask for an order.
fn sort_field(data: &Value) -> Result<Option<SortField>, V6Error> {
    let Some(raw) = opt_str(data, "sort")? else {
        return Ok(None);
    };
    SortField::parse(raw).map(Some).ok_or_else(|| {
        V6Error::field(
            ErrorCode::InvalidField,
            "sort",
            format!("unknown sort field: {raw}"),
        )
    })
}

/// Whether `order` asks for descending. Absent is ascending.
fn descending(data: &Value) -> Result<bool, V6Error> {
    match opt_str(data, "order")? {
        None | Some("asc") => Ok(false),
        Some("desc") => Ok(true),
        Some(other) => Err(V6Error::field(
            ErrorCode::InvalidField,
            "order",
            format!("unknown order: {other}"),
        )),
    }
}

// ── helpers ─────────────────────────────────────────────────────────────────

/// The `query` parameter, lowercased once for the whole op. A blank query is
/// no query: a search box that has been typed into and cleared must not read as
/// a filter that matches nothing.
fn needle(data: &Value) -> Result<Option<String>, V6Error> {
    Ok(opt_str(data, "query")?
        .map(|q| q.trim().to_lowercase())
        .filter(|q| !q.is_empty()))
}

/// Case-insensitive substring test against an already-lowercased needle.
fn contains(haystack: &str, needle: &str) -> bool {
    haystack.to_lowercase().contains(needle)
}

/// Orders a searched list by how well each name answers it, keeping the order
/// the level asked for as the tiebreak within each band.
fn rank_by_relevance<T>(all: &mut [T], needle: &str, name: impl Fn(&T) -> &str + Copy) {
    all.sort_by_key(|entry| relevance(name(entry), needle));
}

/// Slice a full list to `[offset, offset+limit)` (`limit <= 0` = to the end).
fn slice<T>(all: Vec<T>, offset: i64, limit: i64) -> Vec<T> {
    let start = (offset.max(0) as usize).min(all.len());
    let take = if limit > 0 { limit as usize } else { all.len() };
    all.into_iter().skip(start).take(take).collect()
}

/// Read a flat browse list: reuse the cached full `Page<T>` (the V4 reconcile
/// prewarms these under the same keys), else fetch it from the provider and cache
/// it. Returns the full `Vec<T>`; the caller slices to the page.
fn flat_list<T, F>(cache: Option<&MetadataCache>, key: &str, fetch: F) -> Result<Vec<T>, V6Error>
where
    T: Serialize + DeserializeOwned + Default,
    F: FnOnce() -> Result<Page<T>, String>,
{
    if let Some(c) = cache
        && let Some(page) = c.get::<Page<T>>(key)
    {
        return Ok(page.data);
    }
    let page = fetch().map_err(internal)?;
    if let Some(c) = cache {
        c.put(key, &page);
    }
    Ok(page.data)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::{GenreData, RadioStation, Track, TrackTags};
    use crate::providers::MockProviders;
    use mbrc_wire::v6::ErrorCode;

    fn genre(name: &str) -> GenreData {
        GenreData {
            genre: name.into(),
            count: 1,
        }
    }

    #[test]
    fn genres_paginate_and_return_page_envelope() {
        let m = MockProviders {
            browse_genres: Page {
                total: 3,
                offset: 0,
                limit: 0,
                data: vec![genre("Rock"), genre("Jazz"), genre("Pop")],
            },
            ..Default::default()
        };
        // offset 1, limit 1 -> the middle item, total still 3.
        let out = dispatch(
            "library_genres",
            &json!({ "offset": 1, "limit": 1 }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        assert_eq!(out["total"], 3);
        assert_eq!(out["offset"], 1);
        assert_eq!(out["items"].as_array().unwrap().len(), 1);
        assert_eq!(out["items"][0]["genre"], "Jazz");
    }

    #[test]
    fn a_query_narrows_a_level_and_retotals_it() {
        let m = MockProviders {
            browse_genres: Page {
                total: 3,
                offset: 0,
                limit: 0,
                data: vec![genre("Rock"), genre("Electronic"), genre("Classic Rock")],
            },
            ..Default::default()
        };
        let out = dispatch(
            "library_genres",
            &json!({ "query": "rock" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        // The total is the match count, not the library count: a page of results
        // that claims 3 would page a client into two empty screens.
        assert_eq!(out["total"], 2);
        assert_eq!(out["items"][0]["genre"], "Rock");
        assert_eq!(out["items"][1]["genre"], "Classic Rock");
    }

    #[test]
    fn a_query_composes_with_the_filter_beside_it() {
        let m = MockProviders {
            genre_artists: vec![
                ArtistData {
                    artist: "Miles Davis".into(),
                    count: 5,
                },
                ArtistData {
                    artist: "John Coltrane".into(),
                    count: 3,
                },
            ],
            ..Default::default()
        };
        let out = dispatch(
            "library_artists",
            &json!({ "genre": "Jazz", "query": "col" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        assert_eq!(out["total"], 1);
        assert_eq!(out["items"][0]["artist"], "John Coltrane");
    }

    #[test]
    fn an_album_query_matches_its_artist_too() {
        let m = MockProviders {
            browse_albums: Page {
                total: 2,
                offset: 0,
                limit: 0,
                data: vec![
                    AlbumData {
                        album: "Kind of Blue".into(),
                        artist: "Miles Davis".into(),
                        count: 5,
                    },
                    AlbumData {
                        album: "Blue Train".into(),
                        artist: "John Coltrane".into(),
                        count: 6,
                    },
                ],
            },
            ..Default::default()
        };
        let out = dispatch(
            "library_albums",
            &json!({ "query": "miles" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        assert_eq!(out["total"], 1);
        assert_eq!(out["items"][0]["album"], "Kind of Blue");
    }

    /// A box that has been typed into and cleared must read as no filter, not as
    /// a filter nothing matches.
    #[test]
    fn a_blank_query_is_no_query() {
        let m = MockProviders {
            browse_genres: Page {
                total: 2,
                offset: 0,
                limit: 0,
                data: vec![genre("Rock"), genre("Pop")],
            },
            ..Default::default()
        };
        for query in ["", "   "] {
            let out = dispatch("library_genres", &json!({ "query": query }), &m, None, None)
                .unwrap()
                .unwrap();
            assert_eq!(out["total"], 2, "query {query:?} should not filter");
        }
    }

    #[test]
    fn queueing_a_scope_resolves_it_and_reports_the_count() {
        let m = MockProviders {
            artist_albums: vec![AlbumData {
                album: "Kind of Blue".into(),
                artist: "Miles Davis".into(),
                count: 2,
            }],
            album_tracks: vec![
                Track {
                    src: "a.mp3".into(),
                    artist: "Miles Davis".into(),
                    title: "So What".into(),
                    ..Default::default()
                },
                Track {
                    src: "b.mp3".into(),
                    artist: "Miles Davis".into(),
                    title: "Blue in Green".into(),
                    ..Default::default()
                },
            ],
            ..Default::default()
        };
        let out = dispatch(
            "library_queue",
            &json!({ "artist": "Miles Davis", "mode": "now" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        // The caller named a scope so it would not have to handle the list, so
        // the reply is how many went in, not which.
        assert_eq!(out, json!({ "count": 2 }));
        assert!(m.recorded().iter().any(|c| c.starts_with("queue")));
    }

    /// A record filed under a joint or compilation credit is still that record
    /// when it was reached through one of its artists. Matching the artist
    /// against each track queued five of a twenty-three track soundtrack.
    #[test]
    fn an_album_reached_through_an_artist_queues_whole() {
        let joint = "Adam Skorupa & Krzysztof Wierzynkiewicz";
        let tracks: Vec<Track> = ["a.mp3", "b.mp3", "c.mp3"]
            .iter()
            .enumerate()
            .map(|(i, src)| Track {
                src: (*src).into(),
                // Only the first is credited to him alone.
                artist: if i == 0 {
                    "Adam Skorupa".into()
                } else {
                    joint.into()
                },
                album_artist: joint.into(),
                title: format!("Track {i}"),
                ..Default::default()
            })
            .collect();
        let m = MockProviders {
            album_tracks: tracks,
            ..Default::default()
        };

        let out = dispatch(
            "library_queue",
            &json!({ "album": "The Witcher 2", "artist": "Adam Skorupa", "mode": "now" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();

        assert_eq!(out, json!({ "count": 3 }));
    }

    /// The artist still picks between two records that share a title, which is
    /// the only thing it was ever needed for here.
    #[test]
    fn an_artist_chooses_between_albums_of_the_same_name() {
        let m = MockProviders {
            album_tracks: vec![
                Track {
                    src: "queen.mp3".into(),
                    artist: "Queen".into(),
                    album_artist: "Queen".into(),
                    ..Default::default()
                },
                Track {
                    src: "abba.mp3".into(),
                    artist: "ABBA".into(),
                    album_artist: "ABBA".into(),
                    ..Default::default()
                },
            ],
            ..Default::default()
        };

        let out = dispatch(
            "library_queue",
            &json!({ "album": "Greatest Hits", "artist": "Queen", "mode": "now" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();

        assert_eq!(out, json!({ "count": 1 }));
    }

    /// Three different bands have an album called Live. Listing or queueing one
    /// without saying whose returned all three records as a single album.
    #[test]
    fn an_artist_separates_records_that_share_a_title() {
        let walk = vec![
            Track {
                src: "acdc.mp3".into(),
                artist: "AC/DC".into(),
                album_artist: "AC/DC".into(),
                title: "Jailbreak".into(),
                ..Default::default()
            },
            Track {
                src: "bg.mp3".into(),
                artist: "Blind Guardian".into(),
                album_artist: "Blind Guardian".into(),
                title: "Valhalla".into(),
                ..Default::default()
            },
        ];
        let m = MockProviders {
            album_tracks: walk,
            ..Default::default()
        };

        let listed = dispatch(
            "library_tracks",
            &json!({ "album": "Live", "artist": "AC/DC" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        assert_eq!(listed["total"], 1, "one band's record, not all of them");

        let queued = dispatch(
            "library_queue",
            &json!({ "album": "Live", "artist": "AC/DC", "mode": "last" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        assert_eq!(
            queued,
            json!({ "count": 1 }),
            "the list and the queue agree"
        );
    }

    /// An artist in the list that cannot be opened is a hole. MusicBee reports
    /// no album for a track tagged with neither an artist nor an album, which
    /// left the untagged artist visible and unreachable.
    #[test]
    fn an_artist_the_host_has_no_albums_for_is_answered_from_the_tags() {
        use crate::metadata_cache::MetadataCache;
        use crate::store::Db;

        let dir = std::env::temp_dir().join("mbrc-albums-from-tags");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let cache = MetadataCache::new(Db::open(dir.to_str().unwrap()));
        cache.reconcile(&[], 1);

        cache.replace_track_index(&["/nowhere.mp3".into(), "/adele/1.mp3".into()]);
        cache.put_track_tags(&[
            CachedTags {
                src: "/nowhere.mp3".into(),
                title: "Untitled".into(),
                ..CachedTags::default()
            },
            CachedTags {
                src: "/adele/1.mp3".into(),
                title: "Hello".into(),
                artist: "Adele".into(),
                album: "25".into(),
                ..CachedTags::default()
            },
        ]);

        // The host has no album for either, the way it has none for a track
        // tagged with neither an artist nor an album.
        let silent = MockProviders::default();
        let out = dispatch(
            "library_albums",
            &json!({ "artist": "" }),
            &silent,
            None,
            Some(&cache),
        )
        .unwrap()
        .unwrap();

        assert_eq!(out["total"], 1, "the untagged artist can be opened");
        assert_eq!(out["items"][0]["album"], "");
        assert_eq!(out["items"][0]["artist"], "");
    }

    /// The tags are the answer of last resort, never a second opinion: what the
    /// host reports is the album list everything else in the app agrees with.
    #[test]
    fn an_album_the_host_reports_is_the_one_served() {
        use crate::metadata_cache::MetadataCache;
        use crate::store::Db;

        let dir = std::env::temp_dir().join("mbrc-albums-host-wins");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let cache = MetadataCache::new(Db::open(dir.to_str().unwrap()));
        cache.reconcile(&[], 1);
        cache.replace_track_index(&["/adele/1.mp3".into()]);
        cache.put_track_tags(&[CachedTags {
            src: "/adele/1.mp3".into(),
            artist: "Adele".into(),
            album: "a stale name".into(),
            ..CachedTags::default()
        }]);

        let m = MockProviders {
            artist_albums: vec![AlbumData {
                album: "25".into(),
                artist: "Adele".into(),
                count: 1,
            }],
            ..Default::default()
        };
        let out = dispatch(
            "library_albums",
            &json!({ "artist": "Adele" }),
            &m,
            None,
            Some(&cache),
        )
        .unwrap()
        .unwrap();

        assert_eq!(out["items"][0]["album"], "25");
    }

    /// Naming an artist and being handed the whole library is a wrong answer,
    /// not a missing filter: the tracks level only narrowed when an album came
    /// with it, so every artist and every genre read as the entire library.
    #[test]
    fn the_tracks_level_narrows_to_an_artist_without_an_album() {
        use crate::metadata_cache::MetadataCache;
        use crate::store::Db;

        let dir = std::env::temp_dir().join("mbrc-tracks-artist-scope");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let cache = MetadataCache::new(Db::open(dir.to_str().unwrap()));
        cache.reconcile(&[], 1);

        let rows = [
            ("/adele/1.mp3", "Hello", "Adele", "Pop"),
            ("/adele/2.mp3", "Someone", "Adele", "Pop"),
            ("/other/3.mp3", "Elsewhere", "Portishead", "Trip Hop"),
        ];
        cache.replace_track_index(
            &rows
                .iter()
                .map(|(p, ..)| (*p).into())
                .collect::<Vec<String>>(),
        );
        cache.put_track_tags(
            &rows
                .iter()
                .map(|(path, title, artist, genre)| CachedTags {
                    src: (*path).into(),
                    title: (*title).into(),
                    artist: (*artist).into(),
                    genre: (*genre).into(),
                    ..CachedTags::default()
                })
                .collect::<Vec<CachedTags>>(),
        );

        let m = MockProviders::default();
        let total = |request: Value| {
            dispatch("library_tracks", &request, &m, None, Some(&cache))
                .unwrap()
                .unwrap()["total"]
                .as_u64()
                .unwrap()
        };

        assert_eq!(total(json!({"artist": "Adele"})), 2, "an artist narrows");
        assert_eq!(total(json!({"genre": "Trip Hop"})), 1, "a genre narrows");
        assert_eq!(total(json!({})), 3, "naming nothing is the whole library");
    }

    /// A search answers by relevance until an order is asked for, and then by
    /// that order, reading the best match first among rows it cannot tell
    /// apart. Without this the scan's own order is served whatever the request
    /// says, and sorting a search does nothing at all.
    #[test]
    fn a_searched_list_can_still_be_ordered() {
        use crate::metadata_cache::MetadataCache;
        use crate::store::Db;

        let dir = std::env::temp_dir().join("mbrc-search-sort");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let cache = MetadataCache::new(Db::open(dir.to_str().unwrap()));
        cache.reconcile(&[], 1);

        let rows = [
            ("/z.mp3", "Valhalla", "Zeta"),
            ("/a.mp3", "Valhalla", "Alpha"),
        ];
        cache.replace_track_index(
            &rows
                .iter()
                .map(|(p, _, _)| (*p).into())
                .collect::<Vec<String>>(),
        );
        cache.put_track_tags(
            &rows
                .iter()
                .map(|(path, title, artist)| CachedTags {
                    src: (*path).into(),
                    title: (*title).into(),
                    artist: (*artist).into(),
                    ..CachedTags::default()
                })
                .collect::<Vec<CachedTags>>(),
        );

        let m = MockProviders {
            tracks_detailed: Vec::new(),
            ..Default::default()
        };
        let ordered = |sort: Value| {
            let paths = cache.track_paths_where(
                &crate::metadata_cache::TrackFilter {
                    query: Some("valhalla"),
                    ..Default::default()
                },
                sort.as_str().and_then(SortField::parse),
                false,
            );
            let _ = &m;
            paths
        };

        assert_eq!(
            ordered(json!("artist")),
            vec!["/a.mp3".to_string(), "/z.mp3".into()],
            "the order asked for decides"
        );
        assert_eq!(
            ordered(json!(null)),
            vec!["/z.mp3".to_string(), "/a.mp3".into()],
            "with none asked for, the scan's own order stands"
        );
    }

    /// Browsing in through a genre must not poison what the artist named there
    /// queues. The walk's tracks carry no genre, so testing one rejected every
    /// track and the whole Genres branch queued nothing.
    #[test]
    fn a_genre_browsed_through_does_not_empty_the_artist_it_led_to() {
        let m = MockProviders {
            artist_albums: vec![AlbumData {
                album: "Silver Age".into(),
                artist: "Bob Mould".into(),
                count: 2,
            }],
            album_tracks: vec![
                Track {
                    src: "a.mp3".into(),
                    artist: "Bob Mould".into(),
                    album_artist: "Bob Mould".into(),
                    ..Default::default()
                },
                Track {
                    src: "b.mp3".into(),
                    artist: "Bob Mould".into(),
                    album_artist: "Bob Mould".into(),
                    ..Default::default()
                },
            ],
            ..Default::default()
        };

        for scope in [
            json!({ "genre": "Alternative", "artist": "Bob Mould", "mode": "last" }),
            json!({ "genre": "Alternative", "artist": "Bob Mould", "album": "Silver Age", "mode": "last" }),
        ] {
            let out = dispatch("library_queue", &scope, &m, None, None)
                .unwrap()
                .unwrap();
            assert_eq!(out, json!({ "count": 2 }), "scope {scope}");
        }
    }

    /// A genre scope asks the library for the genre, not the genre's artists.
    ///
    /// Walking the artists' albums queues nothing at all: `album_tracks` reports
    /// no genre, so the filter that keeps an artist's other-genre records out
    /// rejects every track it is given.
    #[test]
    fn queueing_a_genre_asks_the_library_for_it() {
        let m = MockProviders {
            genre_artists: vec![ArtistData {
                artist: "Miles Davis".into(),
                count: 1,
            }],
            genre_tracks: vec![Track {
                src: "a.mp3".into(),
                artist: "Miles Davis".into(),
                title: "So What".into(),
                genre: "Jazz".into(),
                ..Default::default()
            }],
            ..Default::default()
        };
        let out = dispatch(
            "library_queue",
            &json!({ "genre": "Jazz", "mode": "last" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();

        assert_eq!(out, json!({ "count": 1 }));
        assert!(m.recorded().iter().any(|c| c == "genre_tracks(Jazz)"));
        assert!(!m.recorded().iter().any(|c| c.starts_with("album_tracks")));
    }

    /// The same filters that narrow a list narrow what a queue takes, so
    /// "queue what I am looking at" needs no second vocabulary.
    /// The tag index holds only what browsing has filled, so resolving an artist
    /// from it queues the albums already visited and calls that the artist. The
    /// provider knows the whole of it.
    #[test]
    fn a_named_scope_is_resolved_through_the_provider_not_the_tag_index() {
        let m = MockProviders {
            artist_albums: vec![
                AlbumData {
                    album: "Symphony of Enchanted Lands".into(),
                    artist: "Rhapsody".into(),
                    count: 2,
                },
                AlbumData {
                    album: "Tales From the Emerald Sword Saga".into(),
                    artist: "Rhapsody".into(),
                    count: 2,
                },
            ],
            album_tracks: vec![Track {
                src: "a.mp3".into(),
                artist: "Rhapsody".into(),
                title: "Emerald Sword".into(),
                ..Default::default()
            }],
            ..Default::default()
        };

        let out = dispatch(
            "library_queue",
            &json!({ "artist": "Rhapsody", "mode": "last" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();

        // One track per album, both albums: the artist's albums were walked
        // rather than whatever happened to be cached.
        assert_eq!(out, json!({ "count": 2 }));
        assert_eq!(
            m.recorded()
                .iter()
                .filter(|c| c.starts_with("album_tracks"))
                .count(),
            2
        );
    }

    /// A tribute or compilation reached through one artist carries every other
    /// artist on it. Queueing an artist must not hand back their guests.
    #[test]
    fn a_compilation_does_not_smuggle_other_artists_into_an_artist_scope() {
        let m = MockProviders {
            artist_albums: vec![AlbumData {
                album: "A Tribute".into(),
                artist: "Rhapsody".into(),
                count: 2,
            }],
            album_tracks: vec![
                Track {
                    src: "rhapsody.mp3".into(),
                    artist: "Rhapsody".into(),
                    album_artist: "Various Artists".into(),
                    title: "Emerald Sword".into(),
                    ..Default::default()
                },
                Track {
                    src: "sonata.mp3".into(),
                    artist: "Sonata Arctica".into(),
                    album_artist: "Various Artists".into(),
                    title: "Fullmoon".into(),
                    ..Default::default()
                },
            ],
            ..Default::default()
        };

        let out = dispatch(
            "library_queue",
            &json!({ "artist": "Rhapsody", "mode": "last" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();

        assert_eq!(out, json!({ "count": 1 }));
    }

    /// "The Beatles" files under B, the way MusicBee, the desktop and the app all
    /// do it. Sorting the raw string instead gathers every "The" together, which
    /// is the one ordering nobody means.
    #[test]
    fn a_name_sort_files_under_the_word_that_matters() {
        let m = MockProviders {
            browse_genres: Page {
                total: 3,
                offset: 0,
                limit: 0,
                data: vec![genre("The Blues"), genre("Ambient"), genre("blues rock")],
            },
            ..Default::default()
        };
        let out = dispatch("library_genres", &json!({ "sort": "name" }), &m, None, None)
            .unwrap()
            .unwrap();
        let names: Vec<&str> = out["items"]
            .as_array()
            .unwrap()
            .iter()
            .map(|g| g["genre"].as_str().unwrap())
            .collect();
        assert_eq!(names, ["Ambient", "The Blues", "blues rock"]);
    }

    #[test]
    fn descending_is_the_same_order_the_other_way_round() {
        let m = MockProviders {
            browse_genres: Page {
                total: 3,
                offset: 0,
                limit: 0,
                data: vec![genre("Ambient"), genre("Zydeco"), genre("Metal")],
            },
            ..Default::default()
        };
        let out = dispatch(
            "library_genres",
            &json!({ "sort": "name", "order": "desc" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        assert_eq!(out["items"][0]["genre"], "Zydeco");
        assert_eq!(out["items"][2]["genre"], "Ambient");
    }

    /// An unknown field is refused rather than ignored: a client that asked for
    /// an order and silently got another has no way to notice.
    #[test]
    fn an_unknown_sort_field_is_refused() {
        let m = MockProviders::default();
        for op in ["library_genres", "library_artists", "library_albums"] {
            let err = dispatch(op, &json!({ "sort": "nonsense" }), &m, None, None)
                .unwrap()
                .unwrap_err();
            assert_eq!(err.code, ErrorCode::InvalidField, "{op}");
            assert_eq!(err.field.as_deref(), Some("sort"), "{op}");
        }
    }

    #[test]
    fn an_unknown_order_is_refused() {
        let m = MockProviders::default();
        let err = dispatch(
            "library_genres",
            &json!({ "sort": "name", "order": "sideways" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap_err();
        assert_eq!(err.field.as_deref(), Some("order"));
    }

    /// Albums sort by the artist shown beside them as well as by their own name.
    #[test]
    fn albums_sort_by_the_artist_beside_them() {
        let m = MockProviders {
            browse_albums: Page {
                total: 2,
                offset: 0,
                limit: 0,
                data: vec![
                    AlbumData {
                        album: "Aardvark".into(),
                        artist: "Zappa".into(),
                        count: 1,
                    },
                    AlbumData {
                        album: "Zebra".into(),
                        artist: "Aphex".into(),
                        count: 1,
                    },
                ],
            },
            ..Default::default()
        };
        let out = dispatch(
            "library_albums",
            &json!({ "sort": "artist" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        assert_eq!(out["items"][0]["artist"], "Aphex");
    }

    #[test]
    fn a_queue_scope_honours_the_query_beside_it() {
        let m = MockProviders {
            artist_albums: vec![AlbumData {
                album: "Kind of Blue".into(),
                artist: "Miles Davis".into(),
                count: 2,
            }],
            album_tracks: vec![
                Track {
                    src: "a.mp3".into(),
                    artist: "Miles Davis".into(),
                    title: "So What".into(),
                    ..Default::default()
                },
                Track {
                    src: "b.mp3".into(),
                    artist: "Miles Davis".into(),
                    title: "Blue in Green".into(),
                    ..Default::default()
                },
            ],
            ..Default::default()
        };
        let out = dispatch(
            "library_queue",
            &json!({ "artist": "Miles Davis", "query": "blue", "mode": "last" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        assert_eq!(out, json!({ "count": 1 }));
    }

    /// Queueing nothing must not reach the player: an empty scope that still
    /// called `queue` would clear or interrupt what is playing.
    #[test]
    fn an_empty_scope_queues_nothing_at_all() {
        let m = MockProviders::default();
        let out = dispatch(
            "library_queue",
            &json!({ "artist": "Nobody", "mode": "now" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        assert_eq!(out, json!({ "count": 0 }));
        assert!(!m.recorded().iter().any(|c| c.starts_with("queue")));
    }

    #[test]
    fn an_unknown_queue_mode_is_refused() {
        let m = MockProviders::default();
        let err = dispatch(
            "library_queue",
            &json!({ "artist": "Miles Davis", "mode": "nowish" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::InvalidField);
    }

    #[test]
    fn artists_genre_filter_takes_the_nav_path() {
        let m = MockProviders {
            genre_artists: vec![ArtistData {
                artist: "Miles Davis".into(),
                count: 5,
            }],
            ..Default::default()
        };
        let out = dispatch(
            "library_artists",
            &json!({ "genre": "Jazz" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        assert_eq!(out["total"], 1);
        assert_eq!(out["items"][0]["artist"], "Miles Davis");
        assert!(m.recorded().iter().any(|c| c.starts_with("genre_artists")));
    }

    #[test]
    fn albums_carry_cover_hash_when_the_store_has_it() {
        use crate::cover::{cover_identifier, test_jpeg_bytes};
        use crate::store::Db;
        let dir = std::env::temp_dir().join("mbrc-v6-lib-albums");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.to_string_lossy().into_owned();
        let store = CoverStore::new(Db::open(&path), path.clone());
        let hash = store
            .cache_cover(
                &cover_identifier("Artist", "Album"),
                &test_jpeg_bytes(64, 64),
            )
            .unwrap();

        let m = MockProviders {
            browse_albums: Page {
                total: 1,
                offset: 0,
                limit: 0,
                data: vec![AlbumData {
                    album: "Album".into(),
                    artist: "Artist".into(),
                    count: 10,
                }],
            },
            ..Default::default()
        };
        let out = dispatch("library_albums", &json!({}), &m, Some(&store), None)
            .unwrap()
            .unwrap();
        assert_eq!(out["items"][0]["cover_hash"], hash);
    }

    #[test]
    fn tracks_flat_emits_typed_canonical_tracks() {
        let m = MockProviders {
            track_paths: vec!["a.mp3".into(), "b.mp3".into()],
            tracks_detailed: vec![
                TrackTags {
                    src: "a.mp3".into(),
                    title: "A".into(),
                    year: "2001".into(),
                    duration: "3:00".into(),
                    ..Default::default()
                },
                TrackTags {
                    src: "b.mp3".into(),
                    title: "B".into(),
                    ..Default::default()
                },
            ],
            ..Default::default()
        };
        // No cache -> falls back to track_paths; typed fields come through track_json.
        let out = dispatch("library_tracks", &json!({}), &m, None, None)
            .unwrap()
            .unwrap();
        assert_eq!(out["total"], 2);
        assert_eq!(out["items"][0]["title"], "A");
        assert_eq!(out["items"][0]["year"], 2001);
        assert_eq!(out["items"][0]["duration_ms"], 180_000);
    }

    #[test]
    fn tracks_album_filter_takes_the_nav_path() {
        let m = MockProviders {
            album_tracks: vec![Track {
                src: "x.mp3".into(),
                ..Default::default()
            }],
            tracks_detailed: vec![TrackTags {
                src: "x.mp3".into(),
                title: "X".into(),
                ..Default::default()
            }],
            ..Default::default()
        };
        let out = dispatch(
            "library_tracks",
            &json!({ "album": "Some Album" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        assert_eq!(out["items"][0]["title"], "X");
        assert!(m.recorded().iter().any(|c| c.starts_with("album_tracks")));
    }

    #[test]
    fn radio_maps_the_provider_page() {
        let m = MockProviders {
            radio_stations: Page {
                total: 1,
                offset: 0,
                limit: 0,
                data: vec![RadioStation {
                    name: "Jazz FM".into(),
                    url: "http://x".into(),
                }],
            },
            ..Default::default()
        };
        let out = dispatch("library_radio", &json!({}), &m, None, None)
            .unwrap()
            .unwrap();
        assert_eq!(out["items"][0]["name"], "Jazz FM");
        assert_eq!(out["items"][0]["url"], "http://x");
    }

    #[test]
    fn play_all_calls_the_provider() {
        let m = MockProviders::default();
        let out = dispatch(
            "library_play_all",
            &json!({ "shuffle": true }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        assert_eq!(out, json!({}));
        assert!(m.recorded().iter().any(|c| c.starts_with("play_all")));
    }

    #[test]
    fn invalid_offset_type_is_invalid_field() {
        let m = MockProviders::default();
        let err = dispatch(
            "library_genres",
            &json!({ "offset": "nope" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap_err();
        assert_eq!(err.code, mbrc_wire::v6::ErrorCode::InvalidField);
    }

    #[test]
    fn unknown_op_is_not_in_this_domain() {
        let m = MockProviders::default();
        assert!(dispatch("player_status", &json!({}), &m, None, None).is_none());
    }

    #[test]
    fn every_advertised_op_dispatches() {
        let m = MockProviders::default();
        for op in OPS {
            assert!(
                dispatch(op, &json!({}), &m, None, None).is_some(),
                "advertised op {op} is not dispatched"
            );
        }
    }
}
