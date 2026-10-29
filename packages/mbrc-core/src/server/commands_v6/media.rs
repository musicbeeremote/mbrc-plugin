//! V6 `media` domain: MusicBee's audiobooks, videos and inbox (#145).
//!
//! The music library ops serve the music category only; these serve the other
//! three, from the in-memory listings in [`crate::media`]. A file of any of
//! them is an ordinary library file, so items use the canonical `album` and
//! `track` objects: a book is an album, a chapter a track. Albums, filters and
//! sorts follow the music library's rules, from the same helpers.

use std::collections::HashMap;

use serde_json::{Value, json};

use mbrc_wire::v6::ErrorCode;

use super::library::{QueueRequest, albums_page, descending, needle, slice, sort_field};
use super::{OpResult, V6Error, internal, opt_str, page_args, page_json, req_str, track};
use crate::cover::store::CoverStore;
use crate::media::{MediaLibrary, MediaSource, MediaTags, album_cover_key};
use crate::metadata_cache::{
    AlbumYears, CachedTags, MetadataCache, TrackFilter, album_key, collate, year_or_zero,
};
use crate::protocol::messages::{AlbumData, TrackTags};
use crate::providers::Providers;

/// The op names this domain serves (advertised in the handshake capabilities).
pub const OPS: &[&str] = &[
    "media_sources",
    "media_albums",
    "media_tracks",
    "media_queue",
];

pub fn dispatch(
    op: &str,
    data: &Value,
    p: &dyn Providers,
    store: Option<&CoverStore>,
    cache: Option<&MetadataCache>,
) -> Option<OpResult> {
    let unkept;
    let media = match cache {
        Some(cache) => cache.media(),
        None => {
            unkept = MediaLibrary::default();
            &unkept
        }
    };
    Some(match op {
        "media_sources" => sources(p, media),
        "media_albums" => albums(data, p, media, store),
        "media_tracks" => tracks(data, p, media, store),
        "media_queue" => queue(data, p, media),
        _ => return None,
    })
}

/// Each category with files in it, from the listings as they stand; asking
/// also queues a re-list, which `media_changed` reports on.
fn sources(p: &dyn Providers, media: &MediaLibrary) -> OpResult {
    media.request_relist(&MediaSource::ALL);
    let mut items = Vec::new();
    for source in MediaSource::ALL {
        let count = media.paths(p, source).map_err(internal)?.len();
        if count > 0 {
            items.push(json!({ "source": source.as_str(), "count": count }));
        }
    }
    Ok(json!({ "items": items }))
}

fn source(data: &Value) -> Result<MediaSource, V6Error> {
    let name = req_str(data, "source")?;
    MediaSource::parse(name).ok_or_else(|| {
        V6Error::field(
            ErrorCode::InvalidField,
            "source",
            format!("unknown media source: {name}"),
        )
    })
}

/// A category's albums, grouped as the music cache groups them.
///
/// Files with no album are left out; `media_tracks` lists them.
fn albums(
    data: &Value,
    p: &dyn Providers,
    media: &MediaLibrary,
    store: Option<&CoverStore>,
) -> OpResult {
    let source = source(data)?;
    let (offset, limit) = page_args(data)?;
    let tags = media.tags(p, source).map_err(internal)?;
    let filter = TrackFilter {
        artist: opt_str(data, "artist")?,
        ..TrackFilter::default()
    };

    let mut grouped: Vec<(AlbumData, AlbumYears)> = Vec::new();
    let mut first_file: HashMap<String, &str> = HashMap::new();
    let mut index: HashMap<String, usize> = HashMap::new();
    for (typed, t) in tags.files().filter(|(_, t)| !t.album.is_empty()) {
        if !filter.admits_tags(typed) {
            continue;
        }
        let key = album_key(&t.album_artist, &t.artist, &t.album);
        let at = *index.entry(key.clone()).or_insert_with(|| {
            first_file.insert(key, &t.src);
            let artist = track::album_key_artist(&t.album_artist, &t.artist);
            grouped.push((
                AlbumData {
                    album: t.album.clone(),
                    artist: artist.to_string(),
                    count: 0,
                },
                AlbumYears::default(),
            ));
            grouped.len() - 1
        });
        let (album, years) = &mut grouped[at];
        album.count += 1;
        years.absorb(year_or_zero(&t.year), year_or_zero(&t.original_year));
    }

    let covers = track::Covers::new(store, p);
    albums_page(data, offset, limit, grouped, |al| {
        let key = album_key(&al.artist, &al.artist, &al.album);
        let src = first_file.get(&key)?;
        covers.item(&album_cover_key(source, &key), src)
    })
}

/// A category's files as tracks, narrowed as `media_queue` narrows them.
///
/// Without a `sort`, an album reads in order: album artist, album, then disc
/// and track, so a book's chapters come in sequence.
fn tracks(
    data: &Value,
    p: &dyn Providers,
    media: &MediaLibrary,
    store: Option<&CoverStore>,
) -> OpResult {
    let source = source(data)?;
    let (offset, limit) = page_args(data)?;
    let sort = sort_field(data)?;
    let descending = descending(data)?;
    let tags = media.tags(p, source).map_err(internal)?;
    let mut selected = select(data, &tags)?;
    match sort {
        Some(field) => selected.sort_by(|a, b| field.compare(a.0, b.0)),
        None => selected.sort_by_cached_key(|(t, _)| reading_order(t)),
    }
    if descending {
        selected.reverse();
    }

    let total = selected.len();
    let page = slice(selected, offset, limit);
    let first_file = first_files(&tags.raw, &page);
    let covers = track::Covers::new(store, p);
    let items = page
        .iter()
        .map(|(typed, t)| {
            let cover = if t.album.is_empty() {
                covers.track(t)
            } else {
                let key = album_key(&typed.album_artist, &typed.artist, &typed.album);
                first_file
                    .get(&key)
                    .and_then(|src| covers.item(&album_cover_key(source, &key), src))
            };
            track::track_json(t, cover.as_deref(), track::Credits::Lead)
        })
        .collect();
    Ok(page_json(total, offset, items))
}

/// Queues a category's files, narrowed as `media_tracks` narrows them, in its
/// reading order.
fn queue(data: &Value, p: &dyn Providers, media: &MediaLibrary) -> OpResult {
    let source = source(data)?;
    let request = QueueRequest::parse(data)?;
    let tags = media.tags(p, source).map_err(internal)?;
    let mut selected = select(data, &tags)?;
    selected.sort_by_cached_key(|(t, _)| reading_order(t));
    let paths = selected.into_iter().map(|(_, t)| t.src.clone()).collect();
    request.queue(p, paths)
}

/// The files an `album`, `artist` and `query` narrow a category to, by the
/// music library's [`TrackFilter`].
fn select<'t>(
    data: &Value,
    tags: &'t MediaTags,
) -> Result<Vec<(&'t CachedTags, &'t TrackTags)>, V6Error> {
    let needle = needle(data)?;
    let filter = TrackFilter {
        query: needle.as_deref(),
        artist: opt_str(data, "artist")?,
        album: opt_str(data, "album")?,
        genre: None,
    };
    Ok(tags
        .files()
        .filter(|(typed, _)| filter.admits_tags(typed))
        .collect())
}

fn reading_order(t: &CachedTags) -> (String, String, i32, i32, String) {
    (
        collate(track::album_key_artist(&t.album_artist, &t.artist)),
        collate(&t.album),
        t.disc_no,
        t.track_no,
        collate(&t.title),
    )
}

/// The first file of each album on `page`, whose artwork stands for the album.
fn first_files<'t>(
    tags: &'t [TrackTags],
    page: &[(&CachedTags, &TrackTags)],
) -> HashMap<String, &'t str> {
    let mut wanted: HashMap<String, Option<&str>> = page
        .iter()
        .filter(|(t, _)| !t.album.is_empty())
        .map(|(t, _)| (album_key(&t.album_artist, &t.artist, &t.album), None))
        .collect();
    let mut missing = wanted.len();
    for t in tags.iter().filter(|t| !t.album.is_empty()) {
        if missing == 0 {
            break;
        }
        if let Some(slot @ None) = wanted.get_mut(&album_key(&t.album_artist, &t.artist, &t.album))
        {
            *slot = Some(&t.src);
            missing -= 1;
        }
    }
    wanted
        .into_iter()
        .filter_map(|(key, src)| Some((key, src?)))
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::providers::MockProviders;

    fn chapter(src: &str, book: &str, author: &str, disc: i32, track: i32) -> TrackTags {
        TrackTags {
            src: src.into(),
            title: src.into(),
            album: book.into(),
            album_artist: author.into(),
            artist: author.into(),
            disc_no: disc,
            track_no: track,
            ..Default::default()
        }
    }

    fn audiobooks(tags: Vec<TrackTags>) -> MockProviders {
        let m = MockProviders {
            tracks_detailed: tags.clone(),
            ..Default::default()
        };
        m.media_paths.lock().unwrap().insert(
            crate::ffi::types::LibrarySource::Audiobooks as i32,
            tags.into_iter().map(|t| t.src).collect(),
        );
        m
    }

    fn call(m: &MockProviders, op: &str, data: Value) -> OpResult {
        dispatch(op, &data, m, None, None).unwrap()
    }

    #[test]
    fn sources_lists_only_categories_with_files() {
        let m = audiobooks(vec![chapter("/b/1.mp3", "Dune", "Herbert", 1, 1)]);

        let out = call(&m, "media_sources", json!({})).unwrap();

        assert_eq!(
            out,
            json!({ "items": [{ "source": "audiobooks", "count": 1 }] })
        );
    }

    /// Asking for the sources answers from the listings and leaves the re-list
    /// to the media task, which reports what it finds with `media_changed`.
    #[test]
    fn sources_queues_a_relist_of_every_category() {
        let m = audiobooks(vec![chapter("/b/1.mp3", "Dune", "Herbert", 1, 1)]);
        let dir = std::env::temp_dir().join(format!("mbrc-media-ops-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let cache = MetadataCache::new(crate::store::Db::open(dir.to_str().unwrap()));

        dispatch("media_sources", &json!({}), &m, None, Some(&cache))
            .unwrap()
            .unwrap();

        let pending = cache.media().take_pending();
        assert_eq!(
            pending.relist.into_iter().collect::<Vec<_>>(),
            MediaSource::ALL
        );
    }

    #[test]
    fn an_empty_category_is_an_empty_page_not_an_error() {
        let m = audiobooks(Vec::new());

        let out = call(&m, "media_albums", json!({ "source": "videos" })).unwrap();

        assert_eq!(out["total"], 0);
    }

    #[test]
    fn an_unknown_source_is_an_invalid_field() {
        let m = audiobooks(Vec::new());

        let err = call(&m, "media_tracks", json!({ "source": "music" })).unwrap_err();

        assert_eq!(err.code, ErrorCode::InvalidField);
        assert_eq!(err.field.as_deref(), Some("source"));
    }

    #[test]
    fn books_page_as_albums_with_their_chapter_counts() {
        let m = audiobooks(vec![
            chapter("/b/dune2.mp3", "Dune", "Herbert", 1, 2),
            chapter("/b/dune1.mp3", "Dune", "Herbert", 1, 1),
            chapter("/b/emma.mp3", "Emma", "Austen", 1, 1),
        ]);

        let out = call(
            &m,
            "media_albums",
            json!({ "source": "audiobooks", "sort": "name" }),
        )
        .unwrap();

        assert_eq!(out["total"], 2);
        assert_eq!(out["items"][0]["album"], "Dune");
        assert_eq!(out["items"][0]["artist"], "Herbert");
        assert_eq!(out["items"][0]["count"], 2);
        assert_eq!(out["items"][1]["album"], "Emma");
    }

    #[test]
    fn albums_differing_only_in_case_are_one_album() {
        let m = audiobooks(vec![
            chapter("/b/1.mp3", "Dune", "Herbert", 1, 1),
            chapter("/b/2.mp3", "dune", "herbert", 1, 2),
        ]);

        let out = call(&m, "media_albums", json!({ "source": "audiobooks" })).unwrap();

        assert_eq!(out["total"], 1);
        assert_eq!(out["items"][0]["count"], 2);
    }

    #[test]
    fn a_book_reads_in_disc_then_track_order() {
        let m = audiobooks(vec![
            chapter("/b/2-1.mp3", "Dune", "Herbert", 2, 1),
            chapter("/b/1-2.mp3", "Dune", "Herbert", 1, 2),
            chapter("/b/1-1.mp3", "Dune", "Herbert", 1, 1),
            chapter("/b/emma.mp3", "Emma", "Austen", 1, 1),
        ]);

        let out = call(
            &m,
            "media_tracks",
            json!({ "source": "audiobooks", "album": "Dune" }),
        )
        .unwrap();

        let srcs: Vec<&str> = out["items"]
            .as_array()
            .unwrap()
            .iter()
            .map(|t| t["src"].as_str().unwrap())
            .collect();
        assert_eq!(srcs, ["/b/1-1.mp3", "/b/1-2.mp3", "/b/2-1.mp3"]);
    }

    #[test]
    fn queueing_a_book_queues_only_its_chapters_in_order() {
        let m = audiobooks(vec![
            chapter("/b/1-2.mp3", "Dune", "Herbert", 1, 2),
            chapter("/b/emma.mp3", "Emma", "Austen", 1, 1),
            chapter("/b/1-1.mp3", "Dune", "Herbert", 1, 1),
        ]);

        let out = call(
            &m,
            "media_queue",
            json!({ "source": "audiobooks", "album": "Dune", "mode": "now" }),
        )
        .unwrap();

        assert_eq!(out, json!({ "count": 2 }));
        let calls = m.recorded();
        let queued = calls.iter().find(|c| c.starts_with("queue")).unwrap();
        assert!(
            queued.find("1-1.mp3").unwrap() < queued.find("1-2.mp3").unwrap(),
            "{queued}"
        );
        assert!(!queued.contains("emma"), "{queued}");
    }

    #[test]
    fn a_queue_the_op_would_refuse_reads_nothing_first() {
        let m = audiobooks(vec![chapter("/b/1.mp3", "Dune", "Herbert", 1, 1)]);

        let err = call(
            &m,
            "media_queue",
            json!({ "source": "audiobooks", "mode": "sideways" }),
        )
        .unwrap_err();

        assert_eq!(err.code, ErrorCode::InvalidField);
        assert!(m.recorded().is_empty(), "{:?}", m.recorded());
    }

    #[test]
    fn tracks_sort_newest_first_by_date_added() {
        let added = |src: &str, at: &str| TrackTags {
            date_added: at.into(),
            ..chapter(src, "", "Someone", 0, 0)
        };
        let m = audiobooks(vec![
            added("/in/old.mp3", "2026-01-01T00:00:00Z"),
            added("/in/new.mp3", "2026-10-01T00:00:00Z"),
        ]);

        let out = call(
            &m,
            "media_tracks",
            json!({ "source": "audiobooks", "sort": "date_added", "order": "desc" }),
        )
        .unwrap();

        assert_eq!(out["items"][0]["src"], "/in/new.mp3");
    }
}
