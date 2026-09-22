//! V6 playlist domain: list playlists, read one's tracks, play one, and edit them.
//!
//! A playlist row carries two indices, as the now-playing list does: `order` is
//! its place in the playlist and the key a mutation takes, `position` its rank
//! in what was returned. They part only under a `query`, which is exactly when
//! a client would otherwise renumber what it must not.
//!
//! The host enumerates a whole playlist in one call because that is the only
//! shape its API offers, and tags are read for the served window alone, so the
//! cost of a page follows the page. `query` and `totals` are the two answers a
//! window cannot give, and each is opt-in for that reason: both read the
//! playlist's own tags, which the cache makes cheap after the first time.
//!
//! Edits (#115) key on `order` and are guarded by the playlist's `version`, a
//! hash of its ordered paths, so an edit made in MusicBee is caught as well as
//! one made by another client. The core computes the new list and writes it
//! whole; an auto playlist, whose contents are a rule, is refused.

use std::collections::{BTreeSet, HashMap};

use serde_json::{Value, json};

use super::nowplaying_list::orders_highest_first;
use super::{
    OpResult, V6Error, internal, opt_bool, opt_str, page_args, page_json, req_i64, req_str,
    req_str_array, track,
};
use crate::cover::store::CoverStore;
use crate::metadata_cache::{CachedTags, MetadataCache, ordered_fingerprint};
use crate::protocol::messages::PlaylistFiles;
use crate::providers::Providers;
use mbrc_wire::v6::ErrorCode;

/// The op names this domain serves (advertised in the handshake capabilities).
pub const OPS: &[&str] = &[
    "playlist_list",
    "playlist_play",
    "playlist_tracks",
    "playlist_create",
    "playlist_delete",
    "playlist_add_tracks",
    "playlist_remove_tracks",
    "playlist_move_tracks",
    "playlist_set_tracks",
];

/// Dispatch a `playlist_*` op. `None` if `op` is not in this domain.
pub fn dispatch(
    op: &str,
    data: &Value,
    p: &dyn Providers,
    cache: Option<&MetadataCache>,
    store: Option<&CoverStore>,
) -> Option<OpResult> {
    Some(match op {
        "playlist_list" => list(data, p),
        "playlist_play" => play(data, p),
        "playlist_tracks" => tracks(data, p, cache, store),
        "playlist_create" => create(data, p, cache),
        "playlist_delete" => delete(data, p),
        "playlist_add_tracks" => add_tracks(data, p, cache),
        "playlist_remove_tracks" => remove_tracks(data, p),
        "playlist_move_tracks" => move_tracks(data, p),
        "playlist_set_tracks" => set_tracks(data, p, cache),
        _ => return None,
    })
}

fn list(data: &Value, p: &dyn Providers) -> OpResult {
    let (offset, limit) = page_args(data)?;
    // Provider-paginated (like radio); pass the window straight through.
    let page = p
        .playlist_catalog(offset as i32, limit as i32)
        .map_err(internal)?;
    let total = page.total.max(0) as usize;
    let items = page
        .data
        .into_iter()
        .map(|pl| json!({ "url": pl.url, "name": pl.name, "editable": editable(&pl.kind) }))
        .collect();
    Ok(page_json(total, offset, items))
}

/// Whether a playlist of this host format holds a list the core can rewrite.
///
/// An auto playlist is a rule MusicBee evaluates, and a radio playlist a set of
/// streams. `Unknown` is what the host answers for a url it does not know.
fn editable(kind: &str) -> bool {
    !matches!(kind, "Auto" | "Radio") && known(kind)
}

fn known(kind: &str) -> bool {
    !matches!(kind, "Unknown" | "")
}

/// The token a client echoes to say which contents it read.
fn version_of(paths: &[String]) -> String {
    format!("{:016x}", ordered_fingerprint(paths))
}

/// One playlist's tracks, as a `Page` of canonical tracks.
///
/// The two indices each row carries, and what `query` and `totals` cost, are
/// described on the module.
fn tracks(
    data: &Value,
    p: &dyn Providers,
    cache: Option<&MetadataCache>,
    store: Option<&CoverStore>,
) -> OpResult {
    let url = req_str(data, "url")?;
    let (offset, limit) = page_args(data)?;
    let needle = opt_str(data, "query")?.map(str::to_lowercase);
    let field = match opt_str(data, "query_field")? {
        None => QueryField::Any,
        Some(raw) => QueryField::parse(raw).ok_or_else(|| {
            V6Error::field(
                ErrorCode::InvalidField,
                "query_field",
                "query_field must be one of: any, title, artist, album",
            )
        })?,
    };
    let totals = opt_bool(data, "totals")?.unwrap_or(false);

    let files = p.playlist_files(url).map_err(internal)?;
    let version = version_of(&files.paths);

    let whole = needle.is_some() || totals;
    let rows = if whole {
        selected(p, cache, &files.paths, needle.as_deref(), field)?
    } else {
        Vec::new()
    };
    let total = if whole { rows.len() } else { files.paths.len() };

    let start = (offset.max(0) as usize).min(total);
    let take = if limit > 0 { limit as usize } else { total };

    let items: Vec<Value> = if whole {
        rows[start..]
            .iter()
            .take(take)
            .enumerate()
            .map(|(i, (order, t))| item_json(t, *order, start + i, store))
            .collect()
    } else {
        let window: Vec<String> = files.paths[start..].iter().take(take).cloned().collect();
        let tags = track::tags_for_paths(p, cache, &window)?;
        let by_path: HashMap<&str, &CachedTags> =
            tags.iter().map(|t| (t.src.as_str(), t)).collect();
        window
            .iter()
            .enumerate()
            .map(|(i, path)| {
                let untagged = untagged_track(path);
                let t = by_path.get(path.as_str()).copied().unwrap_or(&untagged);
                item_json(t, start + i, start + i, store)
            })
            .collect()
    };

    let mut out = page_json(total, offset, items);
    out["name"] = json!(files.name);
    out["version"] = json!(version);
    out["editable"] = json!(editable(&files.kind));
    if totals {
        let sum: i64 = rows.iter().map(|(_, t)| t.duration_ms.max(0)).sum();
        out["total_duration_ms"] = json!(sum);
    }
    Ok(out)
}

/// A canonical track with the two indices every playlist row carries.
fn item_json(t: &CachedTags, order: usize, position: usize, store: Option<&CoverStore>) -> Value {
    let mut obj = track::cached_track_json(t, track::cached_cover_hash_for(store, t).as_deref());
    obj["order"] = json!(order);
    obj["position"] = json!(position);
    obj
}

/// The whole playlist as `(order, tags)`, narrowed to a query when there is one.
///
/// The order each row keeps is its place in the playlist, not its place in the
/// answer: narrowing changes which rows are shown, never where they live.
fn selected(
    p: &dyn Providers,
    cache: Option<&MetadataCache>,
    paths: &[String],
    needle: Option<&str>,
    field: QueryField,
) -> Result<Vec<(usize, CachedTags)>, super::V6Error> {
    let tags = track::tags_for_paths(p, cache, paths)?;
    let by_path: HashMap<&str, &CachedTags> = tags.iter().map(|t| (t.src.as_str(), t)).collect();
    Ok(paths
        .iter()
        .enumerate()
        .map(|(order, path)| {
            let t = by_path
                .get(path.as_str())
                .copied()
                .cloned()
                .unwrap_or_else(|| untagged_track(path));
            (order, t)
        })
        .filter(|(_, t)| match needle {
            Some(n) => matches(t, n, field),
            None => true,
        })
        .collect())
}

/// Which of a row's names a search reads.
///
/// A string enum rather than a flag per field: a reader picks one column or
/// takes them all, and the two-column case has never been asked for.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum QueryField {
    Any,
    Title,
    Artist,
    Album,
}

impl QueryField {
    fn parse(raw: &str) -> Option<Self> {
        match raw {
            "any" => Some(Self::Any),
            "title" => Some(Self::Title),
            "artist" => Some(Self::Artist),
            "album" => Some(Self::Album),
            _ => None,
        }
    }
}

/// Whether a track answers a search, in the column it was asked about.
fn matches(t: &CachedTags, needle: &str, field: QueryField) -> bool {
    let has = |s: &str| s.to_lowercase().contains(needle);
    match field {
        QueryField::Any => has(&t.title) || has(&t.artist) || has(&t.album),
        QueryField::Title => has(&t.title),
        QueryField::Artist => has(&t.artist),
        QueryField::Album => has(&t.album),
    }
}
/// A row for a path the host could not describe: a canonical track with its
/// tags empty.
///
/// One shape everywhere, so a client parses a page without a second case for a
/// row it cannot read, and the track holds its place rather than shifting every
/// `order` after it.
fn untagged_track(path: &str) -> CachedTags {
    CachedTags {
        src: path.to_string(),
        ..CachedTags::default()
    }
}

fn play(data: &Value, p: &dyn Providers) -> OpResult {
    p.play_playlist(req_str(data, "url")?).map_err(internal)?;
    Ok(json!({}))
}

/// A playlist read for an edit.
///
/// Refused `not_found` for a url the host does not know, `unavailable` when its
/// format cannot be written, and `stale_list` when the request carries a
/// `version` its contents have moved past. Sending no `version` skips the
/// guard, as on the queue.
fn editable_files(data: &Value, p: &dyn Providers, url: &str) -> Result<PlaylistFiles, V6Error> {
    let files = p.playlist_files(url).map_err(internal)?;
    if !known(&files.kind) {
        return Err(V6Error::field(
            ErrorCode::NotFound,
            "url",
            "no playlist at this url",
        ));
    }
    if !editable(&files.kind) {
        return Err(V6Error::new(
            ErrorCode::Unavailable,
            "this playlist is not a list of tracks that can be edited",
        ));
    }
    if let Some(expected) = opt_str(data, "version")?
        && expected != version_of(&files.paths)
    {
        return Err(V6Error::new(
            ErrorCode::StaleList,
            "the playlist changed; re-read it and retry",
        ));
    }
    Ok(files)
}

/// The version after a write, read back rather than predicted, so it is the
/// token the next `playlist_tracks` will serve.
fn written_version(p: &dyn Providers, url: &str) -> Result<String, V6Error> {
    Ok(version_of(&p.playlist_files(url).map_err(internal)?.paths))
}

/// The tracks a request names, or `None` when it names none.
///
/// One of three ways: `paths`; a library scope (`genre`, `artist`, `album`,
/// `query`) resolved as `library_queue` resolves it, so a client never pulls an
/// artist's paths only to send them back; or `now_playing: true`, the whole
/// queue. Naming two is refused rather than guessed between.
fn named_tracks(
    data: &Value,
    p: &dyn Providers,
    cache: Option<&MetadataCache>,
) -> Result<Option<Vec<String>>, V6Error> {
    let by_paths = data.get("paths").is_some();
    let by_scope = ["genre", "artist", "album"]
        .iter()
        .any(|f| data.get(f).is_some())
        || opt_str(data, "query")?.is_some_and(|q| !q.trim().is_empty());
    let by_queue = opt_bool(data, "now_playing")?.unwrap_or(false);
    match (by_paths, by_scope, by_queue) {
        (false, false, false) => Ok(None),
        (true, false, false) => req_str_array(data, "paths").map(Some),
        (false, true, false) => super::library::scope_paths(data, p, cache).map(Some),
        (false, false, true) => p.now_playing_list_paths().map(Some).map_err(internal),
        _ => Err(V6Error::new(
            ErrorCode::InvalidField,
            "name the tracks one way: paths, a library scope, or now_playing",
        )),
    }
}

fn required_tracks(
    data: &Value,
    p: &dyn Providers,
    cache: Option<&MetadataCache>,
) -> Result<Vec<String>, V6Error> {
    named_tracks(data, p, cache)?.ok_or_else(|| {
        V6Error::field(
            ErrorCode::MissingField,
            "paths",
            "name the tracks: paths, a library scope, or now_playing",
        )
    })
}

/// Characters Windows refuses in a file name, which is what a playlist name becomes.
const RESERVED_IN_NAME: &[char] = &['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

fn create(data: &Value, p: &dyn Providers, cache: Option<&MetadataCache>) -> OpResult {
    let name = req_str(data, "name")?.trim();
    if name.is_empty() || name.contains(RESERVED_IN_NAME) {
        return Err(V6Error::field(
            ErrorCode::InvalidField,
            "name",
            "name must not be blank or contain < > : \" / \\ | ? *",
        ));
    }
    let folder = opt_str(data, "folder")?.unwrap_or("");
    let paths = named_tracks(data, p, cache)?.unwrap_or_default();
    let url = p.create_playlist(folder, name, paths).map_err(internal)?;
    let files = p.playlist_files(&url).map_err(internal)?;
    Ok(json!({ "url": url, "name": files.name, "version": version_of(&files.paths) }))
}

fn delete(data: &Value, p: &dyn Providers) -> OpResult {
    p.delete_playlist(req_str(data, "url")?).map_err(internal)?;
    Ok(json!({}))
}

fn add_tracks(data: &Value, p: &dyn Providers, cache: Option<&MetadataCache>) -> OpResult {
    let url = req_str(data, "url")?;
    editable_files(data, p, url)?;
    let paths = required_tracks(data, p, cache)?;
    let added = paths.len();
    if added > 0 {
        p.append_to_playlist(url, paths).map_err(internal)?;
    }
    Ok(json!({ "version": written_version(p, url)?, "added": added }))
}

/// Refuses an order past the end of a playlist of `len` tracks.
fn check_in_range(highest: i32, len: usize, field: &str) -> Result<(), V6Error> {
    if highest as usize >= len {
        return Err(V6Error::field(
            ErrorCode::InvalidField,
            field,
            format!("order {highest} is past the end of a {len}-track playlist"),
        ));
    }
    Ok(())
}

fn remove_tracks(data: &Value, p: &dyn Providers) -> OpResult {
    let url = req_str(data, "url")?;
    let orders = orders_highest_first(data, "orders")?;
    let files = editable_files(data, p, url)?;
    check_in_range(orders[0], files.paths.len(), "orders")?;
    let gone: BTreeSet<usize> = orders.iter().map(|&o| o as usize).collect();
    let kept = files
        .paths
        .into_iter()
        .enumerate()
        .filter(|(i, _)| !gone.contains(i))
        .map(|(_, path)| path)
        .collect();
    p.set_playlist_files(url, kept).map_err(internal)?;
    Ok(json!({ "version": written_version(p, url)?, "removed": orders.len() }))
}

fn move_tracks(data: &Value, p: &dyn Providers) -> OpResult {
    let url = req_str(data, "url")?;
    let mut from = orders_highest_first(data, "from_orders")?;
    from.reverse();
    let to = req_i64(data, "to_order")?;
    let files = editable_files(data, p, url)?;
    let len = files.paths.len();
    check_in_range(from[from.len() - 1], len, "from_orders")?;
    let room = len - from.len();
    if !(0..=room as i64).contains(&to) {
        return Err(V6Error::field(
            ErrorCode::InvalidField,
            "to_order",
            format!(
                "to_order must be 0..={room} when moving {} tracks",
                from.len()
            ),
        ));
    }
    p.set_playlist_files(url, moved_to(files.paths, &from, to as usize))
        .map_err(internal)?;
    Ok(json!({ "version": written_version(p, url)? }))
}

/// The list with the slots in `from` lifted out, keeping their own order, and
/// put back so the first of them lands at `to`.
fn moved_to(paths: Vec<String>, from: &[i32], to: usize) -> Vec<String> {
    let lifted: BTreeSet<usize> = from.iter().map(|&o| o as usize).collect();
    let (moved, mut rest): (Vec<_>, Vec<_>) = paths
        .into_iter()
        .enumerate()
        .partition(|(i, _)| lifted.contains(i));
    let tail = rest.split_off(to);
    rest.into_iter()
        .chain(moved)
        .chain(tail)
        .map(|(_, path)| path)
        .collect()
}

fn set_tracks(data: &Value, p: &dyn Providers, cache: Option<&MetadataCache>) -> OpResult {
    let url = req_str(data, "url")?;
    editable_files(data, p, url)?;
    let paths = required_tracks(data, p, cache)?;
    p.set_playlist_files(url, paths).map_err(internal)?;
    Ok(json!({ "version": written_version(p, url)? }))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::{Page, PlaylistEntry, PlaylistFiles, Track, TrackTags};
    use crate::providers::MockProviders;

    #[test]
    fn list_maps_the_provider_page() {
        let m = MockProviders {
            playlist_catalog: Page {
                total: 1,
                data: vec![PlaylistEntry {
                    url: "playlist://x".into(),
                    name: "X".into(),
                    kind: "Mbp".into(),
                }],
                ..Default::default()
            },
            ..Default::default()
        };
        let out = dispatch("playlist_list", &json!({}), &m, None, None)
            .unwrap()
            .unwrap();
        assert_eq!(out["total"], 1);
        assert_eq!(out["items"][0]["url"], "playlist://x");
        assert_eq!(out["items"][0]["name"], "X");
    }

    #[test]
    fn play_calls_the_provider() {
        let m = MockProviders::default();
        let out = dispatch(
            "playlist_play",
            &json!({ "url": "playlist://x" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        assert_eq!(out, json!({}));
        assert!(
            m.recorded()
                .contains(&"play_playlist(playlist://x)".to_string())
        );
    }

    #[test]
    fn play_missing_url_is_missing_field() {
        let m = MockProviders::default();
        let err = dispatch("playlist_play", &json!({}), &m, None, None)
            .unwrap()
            .unwrap_err();
        assert_eq!(err.code, mbrc_wire::v6::ErrorCode::MissingField);
    }

    fn files(paths: &[&str]) -> MockProviders {
        MockProviders {
            playlist_files: PlaylistFiles {
                name: "My Playlist".into(),
                paths: paths.iter().map(|p| (*p).to_string()).collect(),
                ..Default::default()
            },
            ..Default::default()
        }
    }

    fn cache_at(name: &str) -> MetadataCache {
        use crate::store::Db;
        let dir = std::env::temp_dir().join(name);
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let cache = MetadataCache::new(Db::open(dir.to_str().unwrap()));
        cache.reconcile(&[], 1);
        cache
    }

    fn tracks_of(m: &MockProviders, data: Value, cache: Option<&MetadataCache>) -> Value {
        dispatch("playlist_tracks", &data, m, cache, None)
            .unwrap()
            .unwrap()
    }

    #[test]
    fn tracks_carry_their_playlist_order_and_the_name() {
        let m = files(&["/a.mp3", "/b.mp3", "/c.mp3"]);
        let out = tracks_of(&m, json!({ "url": "playlist://x" }), None);
        assert_eq!(out["total"], 3);
        assert_eq!(out["name"], "My Playlist");
        assert_eq!(out["items"][0]["order"], 0);
        assert_eq!(out["items"][2]["order"], 2);
    }

    /// `order` is the position in the playlist, not in the page, so a mutation
    /// keyed on it means the same thing whatever window read it.
    #[test]
    fn order_is_absolute_across_pages() {
        let m = files(&["/a.mp3", "/b.mp3", "/c.mp3", "/d.mp3"]);
        let out = tracks_of(&m, json!({ "url": "p", "offset": 2, "limit": 2 }), None);
        assert_eq!(out["total"], 4, "total is the playlist, not the page");
        assert_eq!(out["offset"], 2);
        assert_eq!(out["items"].as_array().unwrap().len(), 2);
        assert_eq!(out["items"][0]["order"], 2);
        assert_eq!(out["items"][1]["order"], 3);
    }

    #[test]
    fn an_offset_past_the_end_is_an_empty_page_not_an_error() {
        let m = files(&["/a.mp3"]);
        let out = tracks_of(&m, json!({ "url": "p", "offset": 99 }), None);
        assert_eq!(out["total"], 1);
        assert!(out["items"].as_array().unwrap().is_empty());
    }

    #[test]
    fn an_empty_playlist_pages_to_nothing() {
        let m = files(&[]);
        let out = tracks_of(&m, json!({ "url": "p" }), None);
        assert_eq!(out["total"], 0);
        assert!(out["items"].as_array().unwrap().is_empty());
        assert_eq!(out["name"], "My Playlist");
    }

    /// Moving a track without changing the set is exactly the edit a version has
    /// to notice, so the token cannot be order-independent.
    #[test]
    fn the_version_follows_the_order_not_just_the_set() {
        let same = files(&["/a.mp3", "/b.mp3"]);
        let moved = files(&["/b.mp3", "/a.mp3"]);
        let longer = files(&["/a.mp3", "/b.mp3", "/c.mp3"]);
        let v = |m: &MockProviders| tracks_of(m, json!({ "url": "p" }), None)["version"].clone();
        assert_ne!(v(&same), v(&moved), "a move must change the version");
        assert_ne!(v(&same), v(&longer));
        assert_eq!(
            v(&same),
            v(&files(&["/a.mp3", "/b.mp3"])),
            "and it is stable"
        );
    }

    /// The version describes the whole playlist, so paging through one does not
    /// hand out a different token per page.
    #[test]
    fn the_version_does_not_depend_on_the_window() {
        let m = files(&["/a.mp3", "/b.mp3", "/c.mp3"]);
        let first = tracks_of(&m, json!({ "url": "p", "limit": 1 }), None);
        let second = tracks_of(&m, json!({ "url": "p", "offset": 2, "limit": 1 }), None);
        assert_eq!(first["version"], second["version"]);
    }

    /// The cache answers the page, so a warm playlist costs the host nothing.
    #[test]
    fn a_warm_page_asks_the_host_for_nothing() {
        let cache = cache_at("mbrc-playlist-warm");
        cache.put_track_tags(&[CachedTags {
            src: "/a.mp3".into(),
            title: "Cached Title".into(),
            duration_ms: 225_000,
            ..CachedTags::default()
        }]);

        let m = files(&["/a.mp3"]);
        let out = tracks_of(&m, json!({ "url": "p" }), Some(&cache));

        assert_eq!(out["items"][0]["title"], "Cached Title");
        assert_eq!(
            out["items"][0]["duration_ms"], 225_000,
            "a cached row answers a whole track, duration included"
        );
        assert!(
            !m.recorded()
                .iter()
                .any(|c| c.starts_with("tracks_detailed_for_paths")),
            "the host was asked despite a warm cache: {:?}",
            m.recorded()
        );
    }

    /// A cold path is read from the host and filed, so the next read is warm.
    #[test]
    fn a_cold_page_asks_the_host_and_files_what_it_learns() {
        let cache = cache_at("mbrc-playlist-cold");
        let mut m = files(&["/a.mp3"]);
        m.tracks_detailed = vec![TrackTags {
            src: "/a.mp3".into(),
            title: "From The Host".into(),
            duration: "3:45".into(),
            ..Default::default()
        }];

        let out = tracks_of(&m, json!({ "url": "p" }), Some(&cache));
        assert_eq!(out["items"][0]["title"], "From The Host");
        assert_eq!(out["items"][0]["duration_ms"], 225_000);
        assert_eq!(
            cache.track_tags("/a.mp3").map(|t| t.title),
            Some("From The Host".to_string()),
            "what the host answered was not filed"
        );
    }

    /// Only the misses are asked for: a page half in the cache does not re-read
    /// the half that was already known.
    #[test]
    fn only_the_misses_reach_the_host() {
        let cache = cache_at("mbrc-playlist-mixed");
        cache.put_track_tags(&[CachedTags {
            src: "/a.mp3".into(),
            title: "Known".into(),
            ..CachedTags::default()
        }]);
        let mut m = files(&["/a.mp3", "/b.mp3"]);
        m.tracks_detailed = vec![TrackTags {
            src: "/b.mp3".into(),
            title: "Fetched".into(),
            ..Default::default()
        }];

        tracks_of(&m, json!({ "url": "p" }), Some(&cache));
        assert!(
            m.recorded()
                .contains(&"tracks_detailed_for_paths(1)".to_string()),
            "expected exactly one path fetched, got {:?}",
            m.recorded()
        );
    }

    /// A path the host cannot describe is still a row: it holds its place in the
    /// playlist rather than shifting every `order` after it.
    #[test]
    fn a_track_with_no_tags_keeps_its_place() {
        let m = files(&["/a.mp3", "/gone.mp3", "/c.mp3"]);
        let out = tracks_of(&m, json!({ "url": "p" }), None);
        assert_eq!(out["items"].as_array().unwrap().len(), 3);
        assert_eq!(out["items"][2]["order"], 2);
        let row = &out["items"][1];
        assert_eq!(row["src"], "/gone.mp3");
        assert_eq!(row["title"], "", "an unreadable row is still a whole track");
        assert!(row["duration_ms"].is_null());
        assert!(
            row.get("year").is_some(),
            "every canonical field is present"
        );
    }

    /// A narrowed list shows fewer rows, and each still says where it lives.
    #[test]
    fn a_query_narrows_the_playlist_without_moving_what_is_left() {
        let cache = cache_at("mbrc-playlist-query");
        cache.put_track_tags(&[
            CachedTags {
                src: "/a.mp3".into(),
                title: "Queens".into(),
                ..CachedTags::default()
            },
            CachedTags {
                src: "/b.mp3".into(),
                title: "Maniac".into(),
                ..CachedTags::default()
            },
            CachedTags {
                src: "/c.mp3".into(),
                artist: "Queen".into(),
                ..CachedTags::default()
            },
        ]);
        let m = files(&["/a.mp3", "/b.mp3", "/c.mp3"]);

        let out = tracks_of(&m, json!({ "url": "p", "query": "queen" }), Some(&cache));
        assert_eq!(out["total"], 2, "the total is what the query selected");
        assert_eq!(
            out["items"][0]["order"], 0,
            "still the first track of the playlist"
        );
        assert_eq!(
            out["items"][1]["order"], 2,
            "and this one is still the third"
        );
        assert_eq!(out["items"][0]["position"], 0);
        assert_eq!(
            out["items"][1]["position"], 1,
            "but it is the second row shown"
        );
    }

    /// A search matches a title, an artist or an album, and ignores case.
    #[test]
    fn a_query_reads_the_three_names_a_row_shows() {
        let cache = cache_at("mbrc-playlist-query-fields");
        cache.put_track_tags(&[
            CachedTags {
                src: "/a.mp3".into(),
                title: "Nothing".into(),
                ..CachedTags::default()
            },
            CachedTags {
                src: "/b.mp3".into(),
                artist: "CARAVAN".into(),
                ..CachedTags::default()
            },
            CachedTags {
                src: "/c.mp3".into(),
                album: "caravan palace".into(),
                ..CachedTags::default()
            },
        ]);
        let m = files(&["/a.mp3", "/b.mp3", "/c.mp3"]);
        let out = tracks_of(&m, json!({ "url": "p", "query": "Caravan" }), Some(&cache));
        assert_eq!(out["total"], 2);
    }

    /// A named column reads only that column, so a word in one does not answer
    /// a search of another.
    #[test]
    fn a_named_field_searches_only_that_field() {
        let cache = cache_at("mbrc-playlist-field");
        cache.put_track_tags(&[
            CachedTags {
                src: "/a.mp3".into(),
                title: "Caravan".into(),
                artist: "Someone".into(),
                album: "Elsewhere".into(),
                ..CachedTags::default()
            },
            CachedTags {
                src: "/b.mp3".into(),
                title: "Something".into(),
                artist: "Caravan Palace".into(),
                album: "Elsewhere".into(),
                ..CachedTags::default()
            },
            CachedTags {
                src: "/c.mp3".into(),
                title: "Something".into(),
                artist: "Someone".into(),
                album: "Caravan Sessions".into(),
                ..CachedTags::default()
            },
        ]);
        let m = files(&["/a.mp3", "/b.mp3", "/c.mp3"]);
        let hits = |f: &str| {
            let out = tracks_of(
                &m,
                json!({ "url": "p", "query": "caravan", "query_field": f }),
                Some(&cache),
            );
            out["items"]
                .as_array()
                .unwrap()
                .iter()
                .map(|i| i["order"].as_i64().unwrap())
                .collect::<Vec<_>>()
        };
        assert_eq!(hits("title"), vec![0]);
        assert_eq!(hits("artist"), vec![1]);
        assert_eq!(hits("album"), vec![2]);
        assert_eq!(hits("any"), vec![0, 1, 2]);
    }

    /// An unnamed field is every field, so a client that never asks is unchanged.
    #[test]
    fn no_field_named_searches_them_all() {
        let cache = cache_at("mbrc-playlist-field-default");
        cache.put_track_tags(&[CachedTags {
            src: "/a.mp3".into(),
            album: "Panic".into(),
            ..CachedTags::default()
        }]);
        let m = files(&["/a.mp3"]);
        let out = tracks_of(&m, json!({ "url": "p", "query": "panic" }), Some(&cache));
        assert_eq!(out["total"], 1);
    }

    #[test]
    fn an_unknown_query_field_is_refused_rather_than_ignored() {
        let m = files(&["/a.mp3"]);
        let err = dispatch(
            "playlist_tracks",
            &json!({ "url": "p", "query": "x", "query_field": "genre" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap_err();
        assert_eq!(err.code, mbrc_wire::v6::ErrorCode::InvalidField);
        assert_eq!(err.field.as_deref(), Some("query_field"));
    }

    #[test]
    fn totals_sum_the_durations_of_what_the_page_counts() {
        let cache = cache_at("mbrc-playlist-totals");
        cache.put_track_tags(&[
            CachedTags {
                src: "/a.mp3".into(),
                duration_ms: 200_000,
                ..CachedTags::default()
            },
            CachedTags {
                src: "/b.mp3".into(),
                duration_ms: 100_000,
                ..CachedTags::default()
            },
        ]);
        let m = files(&["/a.mp3", "/b.mp3"]);

        let out = tracks_of(&m, json!({ "url": "p", "totals": true }), Some(&cache));
        assert_eq!(out["total_duration_ms"], 300_000);
    }

    /// The total describes what the list holds, so narrowing it narrows both.
    #[test]
    fn a_narrowed_total_duration_counts_only_the_rows_left() {
        let cache = cache_at("mbrc-playlist-totals-query");
        cache.put_track_tags(&[
            CachedTags {
                src: "/a.mp3".into(),
                title: "Keep".into(),
                duration_ms: 200_000,
                ..CachedTags::default()
            },
            CachedTags {
                src: "/b.mp3".into(),
                title: "Drop".into(),
                duration_ms: 100_000,
                ..CachedTags::default()
            },
        ]);
        let m = files(&["/a.mp3", "/b.mp3"]);

        let out = tracks_of(
            &m,
            json!({ "url": "p", "totals": true, "query": "keep" }),
            Some(&cache),
        );
        assert_eq!(out["total"], 1);
        assert_eq!(out["total_duration_ms"], 200_000);
    }

    /// Reading a page must not pay for what the page did not ask about.
    #[test]
    fn a_plain_page_reads_only_its_own_window() {
        let mut m = files(&["/a.mp3", "/b.mp3", "/c.mp3", "/d.mp3"]);
        m.tracks_detailed = vec![];
        tracks_of(&m, json!({ "url": "p", "limit": 2 }), None);
        assert!(
            m.recorded()
                .contains(&"tracks_detailed_for_paths(2)".to_string()),
            "a plain page read more than its window: {:?}",
            m.recorded()
        );
    }

    #[test]
    fn tracks_missing_url_is_missing_field() {
        let m = files(&[]);
        let err = dispatch("playlist_tracks", &json!({}), &m, None, None)
            .unwrap()
            .unwrap_err();
        assert_eq!(err.code, mbrc_wire::v6::ErrorCode::MissingField);
    }

    // ── edits (#115) ────────────────────────────────────────────────────────

    const URL: &str = "C:/mb/Playlists/Mix.mbp";

    /// A playlist the host reports as a plain `.mbp`, holding `paths`.
    fn mbp(paths: &[&str]) -> MockProviders {
        let mut m = files(paths);
        m.playlist_files.kind = "Mbp".into();
        m
    }

    fn edit(m: &MockProviders, op: &str, data: Value) -> OpResult {
        dispatch(op, &data, m, None, None).unwrap()
    }

    fn written(m: &MockProviders) -> Vec<String> {
        m.written_playlist
            .lock()
            .unwrap()
            .clone()
            .expect("nothing was written")
    }

    fn writes(m: &MockProviders) -> usize {
        m.recorded()
            .iter()
            .filter(|c| c.starts_with("set_playlist_files") || c.starts_with("append_to_playlist"))
            .count()
    }

    fn version(m: &MockProviders) -> Value {
        tracks_of(m, json!({ "url": URL }), None)["version"].clone()
    }

    #[test]
    fn list_marks_auto_and_unknown_playlists_read_only() {
        let entry = |kind: &str| PlaylistEntry {
            url: format!("{kind}.x"),
            name: kind.into(),
            kind: kind.into(),
        };
        let m = MockProviders {
            playlist_catalog: Page {
                total: 4,
                data: vec![entry("Mbp"), entry("Auto"), entry("Radio"), entry("M3u")],
                ..Default::default()
            },
            ..Default::default()
        };
        let out = dispatch("playlist_list", &json!({}), &m, None, None)
            .unwrap()
            .unwrap();
        let editable: Vec<bool> = out["items"]
            .as_array()
            .unwrap()
            .iter()
            .map(|i| i["editable"].as_bool().unwrap())
            .collect();
        assert_eq!(editable, [true, false, false, true]);
    }

    #[test]
    fn tracks_say_whether_the_playlist_can_be_edited() {
        assert_eq!(
            tracks_of(&mbp(&["/a"]), json!({ "url": URL }), None)["editable"],
            true
        );
        let mut auto = mbp(&["/a"]);
        auto.playlist_files.kind = "Auto".into();
        assert_eq!(
            tracks_of(&auto, json!({ "url": URL }), None)["editable"],
            false
        );
    }

    /// A batch is one host write, whatever its size: `Playlist_RemoveAt` cost a
    /// host call per slot.
    #[test]
    fn remove_writes_the_list_without_the_named_orders_once() {
        let m = mbp(&["/a", "/b", "/c", "/d", "/e"]);
        let out = edit(
            &m,
            "playlist_remove_tracks",
            json!({ "url": URL, "orders": [3, 1] }),
        )
        .unwrap();
        assert_eq!(written(&m), ["/a", "/c", "/e"]);
        assert_eq!(writes(&m), 1);
        assert_eq!(out["removed"], 2);
    }

    /// Two slots holding one track are two entries; removing one keeps the other.
    #[test]
    fn remove_keeps_the_other_copy_of_a_duplicate() {
        let m = mbp(&["/a", "/b", "/a"]);
        edit(
            &m,
            "playlist_remove_tracks",
            json!({ "url": URL, "orders": [2] }),
        )
        .unwrap();
        assert_eq!(written(&m), ["/a", "/b"]);
    }

    /// The reply carries the token the next read will serve, so a client edits
    /// again without reading the playlist first.
    #[test]
    fn an_edit_replies_the_version_the_next_read_serves() {
        let m = mbp(&["/a", "/b", "/c"]);
        let out = edit(
            &m,
            "playlist_remove_tracks",
            json!({ "url": URL, "orders": [0] }),
        )
        .unwrap();
        assert_eq!(out["version"], version(&m));
    }

    #[test]
    fn a_stale_version_is_refused_and_nothing_is_written() {
        let m = mbp(&["/a", "/b"]);
        let err = edit(
            &m,
            "playlist_remove_tracks",
            json!({ "url": URL, "orders": [0], "version": "0000000000000000" }),
        )
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::StaleList);
        assert_eq!(writes(&m), 0);
    }

    #[test]
    fn the_version_that_was_read_is_accepted() {
        let m = mbp(&["/a", "/b"]);
        let v = version(&m);
        edit(
            &m,
            "playlist_remove_tracks",
            json!({ "url": URL, "orders": [0], "version": v }),
        )
        .unwrap();
        assert_eq!(written(&m), ["/b"]);
    }

    #[test]
    fn an_order_past_the_end_is_refused_before_anything_is_written() {
        let m = mbp(&["/a", "/b"]);
        let err = edit(
            &m,
            "playlist_remove_tracks",
            json!({ "url": URL, "orders": [0, 2] }),
        )
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::InvalidField);
        assert_eq!(err.field.as_deref(), Some("orders"));
        assert_eq!(writes(&m), 0);
    }

    /// An auto playlist is a rule, not a list; writing paths to it would replace the rule.
    #[test]
    fn an_auto_playlist_is_refused_unavailable() {
        let mut m = mbp(&["/a"]);
        m.playlist_files.kind = "Auto".into();
        for (op, data) in [
            (
                "playlist_remove_tracks",
                json!({ "url": URL, "orders": [0] }),
            ),
            (
                "playlist_add_tracks",
                json!({ "url": URL, "paths": ["/b"] }),
            ),
            (
                "playlist_set_tracks",
                json!({ "url": URL, "paths": ["/b"] }),
            ),
            (
                "playlist_move_tracks",
                json!({ "url": URL, "from_orders": [0], "to_order": 0 }),
            ),
        ] {
            assert_eq!(
                edit(&m, op, data).unwrap_err().code,
                ErrorCode::Unavailable,
                "{op}"
            );
        }
        assert_eq!(writes(&m), 0);
    }

    /// The host names the format of a url it does not know `Unknown`.
    #[test]
    fn a_url_the_host_does_not_know_is_not_found() {
        let mut m = mbp(&[]);
        m.playlist_files.kind = "Unknown".into();
        let err = edit(
            &m,
            "playlist_add_tracks",
            json!({ "url": "C:/nope.mbp", "paths": ["/a"] }),
        )
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::NotFound);
    }

    fn moved(paths: &[&str], from: &[i64], to: i64) -> Vec<String> {
        let m = mbp(paths);
        edit(
            &m,
            "playlist_move_tracks",
            json!({ "url": URL, "from_orders": from, "to_order": to }),
        )
        .unwrap();
        written(&m)
    }

    #[test]
    fn a_track_moved_down_lands_at_to_order() {
        assert_eq!(
            moved(&["a", "b", "c", "d", "e"], &[0], 3),
            ["b", "c", "d", "a", "e"]
        );
    }

    /// The direction `Playlist_MoveFiles` lands one slot short.
    #[test]
    fn a_track_moved_up_lands_at_to_order() {
        assert_eq!(
            moved(&["a", "b", "c", "d", "e"], &[3], 1),
            ["a", "d", "b", "c", "e"]
        );
    }

    #[test]
    fn tracks_moved_together_keep_their_own_order_and_land_first_at_to_order() {
        assert_eq!(
            moved(&["a", "b", "c", "d", "e"], &[4, 1], 0),
            ["b", "e", "a", "c", "d"]
        );
        assert_eq!(
            moved(&["a", "b", "c", "d", "e"], &[0, 1], 3),
            ["c", "d", "e", "a", "b"]
        );
    }

    #[test]
    fn a_to_order_past_the_room_left_is_refused() {
        let m = mbp(&["a", "b", "c"]);
        let err = edit(
            &m,
            "playlist_move_tracks",
            json!({ "url": URL, "from_orders": [0, 1], "to_order": 2 }),
        )
        .unwrap_err();
        assert_eq!(err.field.as_deref(), Some("to_order"));
        assert_eq!(writes(&m), 0);
    }

    #[test]
    fn add_appends_the_paths_and_counts_them() {
        let m = mbp(&["/a"]);
        let out = edit(
            &m,
            "playlist_add_tracks",
            json!({ "url": URL, "paths": ["/b", "/c"] }),
        )
        .unwrap();
        assert_eq!(written(&m), ["/a", "/b", "/c"]);
        assert_eq!(out["added"], 2);
        assert_eq!(out["version"], version(&m));
    }

    /// An album is resolved where the library is, so a client never pulls its
    /// paths only to send them back.
    #[test]
    fn add_resolves_a_library_scope_on_the_server() {
        let mut m = mbp(&[]);
        m.album_tracks = ["/x/1.mp3", "/x/2.mp3"]
            .iter()
            .map(|src| Track {
                src: (*src).into(),
                artist: "X".into(),
                album: "Album".into(),
                album_artist: "X".into(),
                ..Default::default()
            })
            .collect();
        let out = edit(
            &m,
            "playlist_add_tracks",
            json!({ "url": URL, "artist": "X", "album": "Album" }),
        )
        .unwrap();
        assert_eq!(out["added"], 2);
        assert_eq!(written(&m), ["/x/1.mp3", "/x/2.mp3"]);
    }

    #[test]
    fn add_from_now_playing_takes_the_whole_queue() {
        let mut m = mbp(&[]);
        m.now_playing_list_paths = vec!["/q1".into(), "/q2".into(), "/q3".into()];
        edit(
            &m,
            "playlist_add_tracks",
            json!({ "url": URL, "now_playing": true }),
        )
        .unwrap();
        assert_eq!(written(&m), ["/q1", "/q2", "/q3"]);
    }

    #[test]
    fn tracks_named_two_ways_are_refused_rather_than_guessed_between() {
        let m = mbp(&[]);
        let err = edit(
            &m,
            "playlist_add_tracks",
            json!({ "url": URL, "paths": ["/a"], "now_playing": true }),
        )
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::InvalidField);
    }

    /// A cleared search box is no scope, so it cannot add the whole library.
    #[test]
    fn a_blank_query_names_no_tracks() {
        let m = mbp(&[]);
        let err = edit(
            &m,
            "playlist_add_tracks",
            json!({ "url": URL, "query": "  " }),
        )
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::MissingField);
        assert_eq!(writes(&m), 0);
    }

    #[test]
    fn create_replies_the_url_name_and_version() {
        let mut m = mbp(&[]);
        m.playlist_files.name = "Road Trip".into();
        let out = edit(
            &m,
            "playlist_create",
            json!({ "name": "Road Trip", "folder": "Trips", "paths": ["/a", "/b"] }),
        )
        .unwrap();
        assert_eq!(out["url"], "Trips/Road Trip.mbp");
        assert_eq!(out["name"], "Road Trip");
        assert_eq!(out["version"], version_of(&["/a".into(), "/b".into()]));
        assert!(
            m.recorded()
                .contains(&"create_playlist(Trips, Road Trip, 2)".to_string())
        );
    }

    #[test]
    fn saving_the_queue_creates_a_playlist_of_it() {
        let mut m = mbp(&[]);
        m.now_playing_list_paths = vec!["/q1".into(), "/q2".into()];
        edit(
            &m,
            "playlist_create",
            json!({ "name": "Tonight", "now_playing": true }),
        )
        .unwrap();
        assert_eq!(written(&m), ["/q1", "/q2"]);
    }

    #[test]
    fn create_without_tracks_makes_an_empty_playlist() {
        let m = mbp(&[]);
        edit(&m, "playlist_create", json!({ "name": "Empty" })).unwrap();
        assert!(
            m.recorded()
                .contains(&"create_playlist(, Empty, 0)".to_string())
        );
    }

    #[test]
    fn create_refuses_a_name_windows_cannot_file() {
        let m = mbp(&[]);
        for name in ["", "   ", "a/b", "a\\b", "what?"] {
            let err = edit(&m, "playlist_create", json!({ "name": name })).unwrap_err();
            assert_eq!(err.field.as_deref(), Some("name"), "{name:?}");
        }
        assert!(
            !m.recorded()
                .iter()
                .any(|c| c.starts_with("create_playlist"))
        );
    }

    #[test]
    fn set_replaces_the_contents_in_the_order_sent() {
        let m = mbp(&["/a", "/b"]);
        edit(
            &m,
            "playlist_set_tracks",
            json!({ "url": URL, "paths": ["/c", "/a"] }),
        )
        .unwrap();
        assert_eq!(written(&m), ["/c", "/a"]);
    }

    #[test]
    fn delete_asks_the_host() {
        let m = mbp(&[]);
        assert_eq!(
            edit(&m, "playlist_delete", json!({ "url": URL })).unwrap(),
            json!({})
        );
        assert!(m.recorded().contains(&format!("delete_playlist({URL})")));
    }

    #[test]
    fn a_write_the_host_refuses_is_internal() {
        let mut m = mbp(&["/a"]);
        m.refuse_playlist_writes = true;
        let err = edit(
            &m,
            "playlist_set_tracks",
            json!({ "url": URL, "paths": ["/b"] }),
        )
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::Internal);
    }

    #[test]
    fn unknown_op_is_not_in_this_domain() {
        let m = MockProviders::default();
        assert!(dispatch("library_genres", &json!({}), &m, None, None).is_none());
    }

    #[test]
    fn every_advertised_op_dispatches() {
        let m = MockProviders::default();
        for op in OPS {
            assert!(
                dispatch(op, &json!({ "url": "x" }), &m, None, None).is_some(),
                "advertised op {op} is not dispatched"
            );
        }
    }
}
