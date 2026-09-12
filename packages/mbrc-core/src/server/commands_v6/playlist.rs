//! V6 playlist domain: list playlists, read one's tracks, and play one.
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
//! The playlist mutations (#115) are a feature request rather than a V4
//! feature, and are not served here.

use std::collections::HashMap;

use serde_json::{Value, json};

use super::{OpResult, V6Error, internal, opt_bool, opt_str, page_args, page_json, req_str, track};
use crate::cover::store::CoverStore;
use crate::metadata_cache::{CachedTags, MetadataCache, ordered_fingerprint};
use crate::providers::Providers;
use mbrc_wire::v6::ErrorCode;

/// The op names this domain serves (advertised in the handshake capabilities).
pub const OPS: &[&str] = &["playlist_list", "playlist_play", "playlist_tracks"];

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
        _ => return None,
    })
}

fn list(data: &Value, p: &dyn Providers) -> OpResult {
    let (offset, limit) = page_args(data)?;
    // Provider-paginated (like radio); pass the window straight through.
    let page = p.playlists(offset as i32, limit as i32).map_err(internal)?;
    let total = page.total.max(0) as usize;
    let items = page
        .data
        .into_iter()
        .map(|pl| json!({ "url": pl.url, "name": pl.name }))
        .collect();
    Ok(page_json(total, offset, items))
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
    let version = format!("{:016x}", ordered_fingerprint(&files.paths));

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

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::{Page, Playlist, PlaylistFiles, TrackTags};
    use crate::providers::MockProviders;

    #[test]
    fn list_maps_the_provider_page() {
        let m = MockProviders {
            playlists: Page {
                total: 1,
                data: vec![Playlist {
                    url: "playlist://x".into(),
                    name: "X".into(),
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
