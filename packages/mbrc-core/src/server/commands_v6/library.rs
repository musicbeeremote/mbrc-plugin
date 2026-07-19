//! V6 library domain: browse the library as paginated lists. Unifies V4's separate
//! flat-browse and hierarchical-nav ops into one op per level with an optional
//! filter (#118's "one canonical list + parameter" principle):
//!
//! - `library_artists { genre? }` - all artists, or a genre's artists
//! - `library_albums { artist? }` - all albums, or an artist's albums
//! - `library_tracks { album? }` - all tracks (canonical `track`), or an album's
//!
//! Plus `library_genres`, `library_radio`, and `library_play_all`.
//!
//! Every list is a V6 `Page` (`{ total, offset, items }`). Track items are the
//! canonical `track` (`track::track_json`); album items carry a lazily-resolved
//! `cover_hash`. All reads reuse existing provider methods + the metadata cache.

use serde::de::DeserializeOwned;
use serde::Serialize;
use serde_json::{json, Value};

use super::{internal, opt_bool, opt_str, page_args, page_json, track, OpResult, V6Error};
use crate::cover::store::CoverStore;
use crate::metadata_cache::MetadataCache;
use crate::protocol::messages::{AlbumData, ArtistData, Page};
use crate::providers::Providers;
use crate::server::commands::library::{key_browse_artists, KEY_BROWSE_ALBUMS, KEY_BROWSE_GENRES};

/// The op names this domain serves (advertised in the handshake capabilities).
pub const OPS: &[&str] = &[
    "library_genres",
    "library_artists",
    "library_albums",
    "library_tracks",
    "library_radio",
    "library_play_all",
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
        _ => return None,
    })
}

fn genres(data: &Value, p: &dyn Providers, cache: Option<&MetadataCache>) -> OpResult {
    let (offset, limit) = page_args(data)?;
    let all = flat_list(cache, KEY_BROWSE_GENRES, || p.browse_genres(0, 0))?;
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
    let all: Vec<ArtistData> = match opt_str(data, "genre")? {
        Some(genre) => p.genre_artists(genre).map_err(internal)?,
        None => {
            let album_artists = opt_bool(data, "album_artists")?.unwrap_or(false);
            flat_list(cache, &key_browse_artists(album_artists), || {
                p.browse_artists(0, 0, album_artists)
            })?
        }
    };
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
        Some(artist) => p.artist_albums(artist).map_err(internal)?,
        None => flat_list(cache, KEY_BROWSE_ALBUMS, || p.browse_albums(0, 0))?,
    };
    let total = all.len();
    let items = slice(all, offset, limit)
        .into_iter()
        .map(|al| {
            let mut obj = json!({ "album": al.album, "artist": al.artist, "count": al.count });
            if let Some(hash) = track::album_cover_hash(store, &al.artist, &al.album) {
                obj["cover_hash"] = json!(hash);
            }
            obj
        })
        .collect();
    Ok(page_json(total, offset, items))
}

fn tracks(
    data: &Value,
    p: &dyn Providers,
    store: Option<&CoverStore>,
    cache: Option<&MetadataCache>,
) -> OpResult {
    let (offset, limit) = page_args(data)?;
    // Resolve the scope's ordered paths, sliced to the page.
    let (total, page_paths): (usize, Vec<String>) = match opt_str(data, "album")? {
        Some(album) => {
            let paths: Vec<String> = p
                .album_tracks(album)
                .map_err(internal)?
                .into_iter()
                .map(|t| t.src)
                .collect();
            let total = paths.len();
            (total, slice(paths, offset, limit))
        }
        None => match cache {
            // O(page) via the ordinal index once the reconcile has built it.
            Some(c) if c.track_count() > 0 => (
                c.track_count() as usize,
                c.track_page_paths(offset as i32, limit as i32),
            ),
            // Cold cache: fall back to the full path list, sliced.
            _ => {
                let paths = p.track_paths().map_err(internal)?;
                let total = paths.len();
                (total, slice(paths, offset, limit))
            }
        },
    };
    // One batch read for the page's typed tags -> canonical tracks.
    let tags = p.tracks_detailed_for_paths(page_paths).map_err(internal)?;
    let items = tags
        .iter()
        .map(|t| track::track_json(t, track::cover_hash_for(store, t).as_deref()))
        .collect();
    Ok(page_json(total, offset, items))
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

fn play_all(data: &Value, p: &dyn Providers) -> OpResult {
    let shuffle = opt_bool(data, "shuffle")?.unwrap_or(false);
    p.play_all(shuffle).map_err(internal)?;
    Ok(json!({}))
}

// ── helpers ─────────────────────────────────────────────────────────────────

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
    if let Some(c) = cache {
        if let Some(page) = c.get::<Page<T>>(key) {
            return Ok(page.data);
        }
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
