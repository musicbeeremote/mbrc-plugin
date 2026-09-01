//! V6 now-playing LIST domain: the play queue.
//!
//! Per #118 §7 this is ONE canonical list, with no `client_type` fork - the V4
//! ordered/sequential and album-drop quirks dissolve - plus play / remove / move
//! / search and queueing. Mutations key on `order`; #110 versioned order is not
//! served here.
//!
//! Two views, selected by `up_next`: the default FULL list in list order,
//! already-played tracks included, and the shuffle-aware play order from the
//! current track, which drops them. Every item carries three indices:
//!
//! - `order`, the absolute storage index, and the key every mutation consumes.
//! - `position`, the 0-based rank within the presented window.
//! - `play_position`, the rank in shuffle play order (0 = current), or -1 for an
//!   already-played track.
//!
//! One view that is both play order and keeps already-played is impossible:
//! `GetNextIndex` is forward-only, so a played track's play order is
//! unrecoverable - hence the -1 rather than a reordered full list.

use std::collections::HashMap;

use serde_json::{Value, json};

use super::{
    OpResult, V6Error, i32_saturating, internal, opt_bool, opt_i64, page_args, page_json, req_i64,
    req_str, req_str_array, track,
};
use crate::cover::store::CoverStore;
use crate::nowplaying::NowPlayingCache;
use crate::protocol::messages::{NowPlayingListTrack, QueueType, TrackTags};
use crate::providers::Providers;
use mbrc_wire::v6::ErrorCode;

/// The op names this domain serves (advertised in the handshake capabilities).
pub const OPS: &[&str] = &[
    "now_playing_list",
    "now_playing_list_play",
    "now_playing_list_remove",
    "now_playing_list_move",
    "now_playing_list_search",
    "now_playing_queue",
];

/// Dispatch a now-playing-list op. `None` if `op` is not in this domain.
pub fn dispatch(
    op: &str,
    data: &Value,
    p: &dyn Providers,
    now_playing: Option<&NowPlayingCache>,
    cover_store: Option<&CoverStore>,
) -> Option<OpResult> {
    Some(match op {
        "now_playing_list" => list(data, p, now_playing, cover_store),
        "now_playing_list_play" => play(data, p, now_playing),
        "now_playing_list_remove" => remove(data, p, now_playing),
        "now_playing_list_move" => move_item(data, p, now_playing),
        "now_playing_list_search" => search(data, p),
        "now_playing_queue" => queue(data, p),
        _ => return None,
    })
}

/// The now-playing queue as a `Page` of canonical tracks.
///
/// The two views and the three per-item indices are described on the module.
fn list(
    data: &Value,
    p: &dyn Providers,
    now_playing: Option<&NowPlayingCache>,
    store: Option<&CoverStore>,
) -> OpResult {
    let (offset, limit) = page_args(data)?;
    let up_next = opt_bool(data, "up_next")?.unwrap_or(false);
    let page = if up_next {
        p.now_playing_list_ordered(offset as i32, limit as i32)
    } else {
        p.now_playing_list(offset as i32, limit as i32)
    }
    .map_err(internal)?;

    let play_rank = if up_next {
        HashMap::new()
    } else {
        play_ranks(p)?
    };

    // A path the library cannot resolve (a queued external file) falls back to
    // its basic fields.
    let paths: Vec<String> = page
        .data
        .iter()
        .map(|t| t.path.clone())
        .filter(|p| !p.is_empty())
        .collect();
    let tags = p.tracks_detailed_for_paths(paths).map_err(internal)?;
    let by_path: HashMap<&str, &TrackTags> = tags.iter().map(|t| (t.src.as_str(), t)).collect();

    let items = page
        .data
        .iter()
        .enumerate()
        .map(|(i, npt)| {
            let mut obj = match by_path.get(npt.path.as_str()) {
                Some(t) => track::track_json(t, track::cover_hash_for(store, t).as_deref()),
                None => basic_track_json(npt),
            };
            let rank = offset + i as i64;
            // The up-next walk carries the storage index in `position`; in list
            // order the display rank already is that index.
            let order = if up_next { npt.position as i64 } else { rank };
            let play_position = if up_next {
                rank
            } else {
                play_rank.get(&(order as i32)).copied().unwrap_or(-1)
            };
            obj["order"] = json!(order);
            obj["position"] = json!(rank);
            obj["play_position"] = json!(play_position);
            obj
        })
        .collect();
    let mut out = page_json(page.total.max(0) as usize, offset, items);
    out["version"] = json!(list_version(now_playing));
    Ok(out)
}

/// The version a page is served with, and the one a mutation is checked against.
///
/// Zero without a cache, which is the test and no-runtime path: a server that
/// cannot track changes reports the same version forever, so a client's guard
/// never fires spuriously.
fn list_version(now_playing: Option<&NowPlayingCache>) -> u64 {
    now_playing.map_or(0, NowPlayingCache::list_version)
}

/// Checks an optional `version` against the queue's, then runs the mutation.
///
/// The guard is opt-in (#118 §7): a client that sends the `version` it read is
/// refused `stale_list` when the queue moved under it, rather than mutating a
/// slot that no longer holds what it saw. Sending none keeps the unguarded
/// behaviour. The bump is here, not left to MusicBee's asynchronous
/// notification, which a mutate-then-read would outrun.
fn guarded(
    data: &Value,
    now_playing: Option<&NowPlayingCache>,
    mutate: impl FnOnce() -> Result<(), String>,
) -> OpResult {
    if let Some(expected) = opt_i64(data, "version")?
        && expected != list_version(now_playing) as i64
    {
        return Err(V6Error::new(
            ErrorCode::StaleList,
            "the now-playing list changed; re-read it and retry",
        ));
    }
    mutate().map_err(internal)?;
    if let Some(cache) = now_playing {
        cache.bump_list_version();
    }
    Ok(json!({}))
}

/// Storage index to shuffle play rank, for the list-order view.
///
/// The forward walk carries the storage index in `position`, and its ordinal is
/// the play rank; a track missing from the walk has been played and gets -1 at
/// the call site. Indices only, but the walk still reads tags - queues are small
/// enough that an indices-only provider has not been worth adding.
fn play_ranks(p: &dyn Providers) -> Result<HashMap<i32, i64>, V6Error> {
    Ok(p.now_playing_list_ordered(0, 0)
        .map_err(internal)?
        .data
        .iter()
        .enumerate()
        .map(|(rank, npt)| (npt.position, rank as i64))
        .collect())
}

/// A minimal canonical-shaped track from the now-playing item's basic fields, for
/// a queued path the library can't resolve to full tags.
fn basic_track_json(npt: &NowPlayingListTrack) -> Value {
    json!({
        "src": npt.path.as_str(),
        "artist": npt.artist.as_str(),
        "title": npt.title.as_str(),
        "album": npt.album.as_str(),
        "album_artist": npt.album_artist.as_str(),
        "track_no": 0,
        "disc_no": 0,
        "genre": "",
        "year": Value::Null,
        "duration_ms": Value::Null,
        "rating": Value::Null,
        "date_added": Value::Null,
    })
}

fn play(data: &Value, p: &dyn Providers, now_playing: Option<&NowPlayingCache>) -> OpResult {
    let order = i32_saturating(req_i64(data, "order")?);
    guarded(data, now_playing, || p.play_list_item(order))
}

fn remove(data: &Value, p: &dyn Providers, now_playing: Option<&NowPlayingCache>) -> OpResult {
    let order = i32_saturating(req_i64(data, "order")?);
    guarded(data, now_playing, || p.remove_list_item(order))
}

fn move_item(data: &Value, p: &dyn Providers, now_playing: Option<&NowPlayingCache>) -> OpResult {
    let from = i32_saturating(req_i64(data, "from")?);
    let to = i32_saturating(req_i64(data, "to")?);
    guarded(data, now_playing, || p.move_list_item(from, to))
}

fn search(data: &Value, p: &dyn Providers) -> OpResult {
    p.search_list(req_str(data, "query")?).map_err(internal)?;
    Ok(json!({}))
}

fn queue(data: &Value, p: &dyn Providers) -> OpResult {
    let paths = req_str_array(data, "paths")?;
    let mode = data.get("mode").and_then(Value::as_str).unwrap_or("next");
    let play = data.get("play").and_then(Value::as_str).unwrap_or("");
    p.queue(parse_queue_type(mode)?, paths, play)
        .map_err(internal)?;
    Ok(json!({}))
}

/// The V6 `mode` string as a queue placement (`next` when absent).
///
/// A closed enum's unknown value is rejected, never coerced (#118 §2.1): a
/// client that misspells a mode is told so, instead of silently queueing to a
/// different place. The V4 wire spells the last one `add-all`; V6 is snake_case
/// throughout (#118 §4), and that spelling stays in the V4 codec alone.
fn parse_queue_type(mode: &str) -> Result<QueueType, V6Error> {
    Ok(match mode {
        "next" => QueueType::Next,
        "last" => QueueType::Last,
        "now" => QueueType::PlayNow,
        "add_all" => QueueType::AddAndPlay,
        other => {
            return Err(V6Error::new(
                ErrorCode::InvalidField,
                format!("unknown queue mode: {other}"),
            ));
        }
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::Page;
    use crate::providers::MockProviders;

    fn npt(path: &str, position: i32) -> NowPlayingListTrack {
        NowPlayingListTrack {
            path: path.into(),
            title: format!("title-{position}"),
            position,
            ..Default::default()
        }
    }

    #[test]
    fn list_emits_canonical_tracks_with_order() {
        let m = MockProviders {
            now_playing_list: Page {
                total: 2,
                offset: 0,
                limit: 0,
                data: vec![npt("a.mp3", 0), npt("b.mp3", 1)],
            },
            // Forward play order = only b remains upcoming (storage index 1),
            // so a (storage 0) is already played -> play_position -1.
            now_playing_list_ordered: Page {
                total: 1,
                offset: 0,
                limit: 0,
                data: vec![npt("b.mp3", 1)],
            },
            tracks_detailed: vec![TrackTags {
                src: "a.mp3".into(),
                title: "Resolved A".into(),
                duration: "3:00".into(),
                ..Default::default()
            }],
            ..Default::default()
        };
        let out = dispatch("now_playing_list", &json!({}), &m, None, None)
            .unwrap()
            .unwrap();
        assert_eq!(out["total"], 2);
        // Default view sources the FULL list (storage order).
        assert!(m.recorded().contains(&"now_playing_list".to_string()));
        // First item resolved to full tags; order == position == storage index 0.
        assert_eq!(out["items"][0]["title"], "Resolved A");
        assert_eq!(out["items"][0]["duration_ms"], 180_000);
        assert_eq!(out["items"][0]["order"], 0);
        assert_eq!(out["items"][0]["position"], 0);
        assert_eq!(out["items"][0]["play_position"], -1); // already played
        // Second item unresolved -> basic-field fallback; order == position == 1.
        assert_eq!(out["items"][1]["title"], "title-1");
        assert_eq!(out["items"][1]["src"], "b.mp3");
        assert_eq!(out["items"][1]["order"], 1);
        assert_eq!(out["items"][1]["position"], 1);
        assert_eq!(out["items"][1]["play_position"], 0); // next up
    }

    #[test]
    fn up_next_uses_ordered_source_with_index_order() {
        // The ordered (shuffle) source carries the true storage index in
        // `position`; those become `order` while `position` is the display rank.
        let m = MockProviders {
            now_playing_list_ordered: Page {
                total: 2,
                offset: 0,
                limit: 0,
                data: vec![npt("x.mp3", 5), npt("y.mp3", 2)],
            },
            ..Default::default()
        };
        let out = dispatch(
            "now_playing_list",
            &json!({ "up_next": true }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        assert!(
            m.recorded()
                .contains(&"now_playing_list_ordered".to_string())
        );
        // order = the (non-contiguous) storage index; position = the rank;
        // play_position == the rank (up-next IS the play order).
        assert_eq!(out["items"][0]["order"], 5);
        assert_eq!(out["items"][0]["position"], 0);
        assert_eq!(out["items"][0]["play_position"], 0);
        assert_eq!(out["items"][1]["order"], 2);
        assert_eq!(out["items"][1]["position"], 1);
        assert_eq!(out["items"][1]["play_position"], 1);
    }

    #[test]
    fn default_view_offsets_order_position_and_play_position() {
        // A page at offset 5: order/position are the absolute storage indices
        // (offset + i), and play_position is looked up by that order.
        let m = MockProviders {
            now_playing_list: Page {
                total: 20,
                offset: 5,
                limit: 2,
                data: vec![npt("f.mp3", 5), npt("g.mp3", 6)],
            },
            // Play order: g (storage 6) is current, f (storage 5) is next.
            now_playing_list_ordered: Page {
                total: 2,
                offset: 0,
                limit: 0,
                data: vec![npt("g.mp3", 6), npt("f.mp3", 5)],
            },
            ..Default::default()
        };
        let out = dispatch(
            "now_playing_list",
            &json!({ "offset": 5, "limit": 2 }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        assert_eq!(out["offset"], 5);
        assert_eq!(out["items"][0]["order"], 5); // offset + 0
        assert_eq!(out["items"][0]["position"], 5);
        assert_eq!(out["items"][0]["play_position"], 1); // storage 5 -> play rank 1
        assert_eq!(out["items"][1]["order"], 6); // offset + 1
        assert_eq!(out["items"][1]["position"], 6);
        assert_eq!(out["items"][1]["play_position"], 0); // storage 6 -> current
    }

    /// The mutations key on `order` - the value the list items carry - so a
    /// client passes back exactly what it read.
    #[test]
    fn play_remove_move_call_providers() {
        let m = MockProviders::default();
        dispatch(
            "now_playing_list_play",
            &json!({ "order": 3 }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        dispatch(
            "now_playing_list_remove",
            &json!({ "order": 2 }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        dispatch(
            "now_playing_list_move",
            &json!({ "from": 1, "to": 4 }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        let calls = m.recorded();
        assert!(calls.contains(&"play_list_item(3)".to_string()));
        assert!(calls.contains(&"remove_list_item(2)".to_string()));
        assert!(calls.iter().any(|c| c.starts_with("move_list_item")));
    }

    #[test]
    fn queue_maps_mode_and_passes_paths() {
        let m = MockProviders::default();
        dispatch(
            "now_playing_queue",
            &json!({ "paths": ["x.mp3", "y.mp3"], "mode": "now" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap();
        assert!(m.recorded().iter().any(|c| c.starts_with("queue")));
    }

    /// A misspelled mode used to queue to `next` silently; a closed enum's
    /// unknown value is rejected, not coerced (#118 §2.1).
    #[test]
    fn queue_unknown_mode_is_invalid_field() {
        let m = MockProviders::default();
        let err = dispatch(
            "now_playing_queue",
            &json!({ "paths": ["x.mp3"], "mode": "nowish" }),
            &m,
            None,
            None,
        )
        .unwrap()
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::InvalidField);
        assert!(!m.recorded().iter().any(|c| c.starts_with("queue")));
    }

    #[test]
    fn queue_missing_paths_is_missing_field() {
        let m = MockProviders::default();
        let err = dispatch("now_playing_queue", &json!({}), &m, None, None)
            .unwrap()
            .unwrap_err();
        assert_eq!(err.code, mbrc_wire::v6::ErrorCode::MissingField);
    }

    #[test]
    fn play_bad_order_is_invalid_or_missing() {
        let m = MockProviders::default();
        let err = dispatch("now_playing_list_play", &json!({}), &m, None, None)
            .unwrap()
            .unwrap_err();
        assert_eq!(err.code, mbrc_wire::v6::ErrorCode::MissingField);
    }

    #[test]
    fn parse_queue_type_maps_modes() {
        assert!(matches!(parse_queue_type("next"), Ok(QueueType::Next)));
        assert!(matches!(parse_queue_type("last"), Ok(QueueType::Last)));
        assert!(matches!(parse_queue_type("now"), Ok(QueueType::PlayNow)));
        assert!(matches!(
            parse_queue_type("add_all"),
            Ok(QueueType::AddAndPlay)
        ));
    }

    /// `add-all` is the V4 wire spelling, parsed by the V4 codec and present in
    /// the shipped goldens. V6 is snake_case (#118 §4), and accepting the V4
    /// spelling here would let it leak back in unnoticed.
    #[test]
    fn the_v4_queue_spelling_is_not_accepted_by_v6() {
        assert!(parse_queue_type("add-all").is_err());
    }

    /// The page carries the version its `order`s belong to (#118 §7), so a
    /// client can hand it back and be told the list moved.
    #[test]
    fn the_page_carries_the_list_version() {
        let providers: std::sync::Arc<dyn Providers> =
            std::sync::Arc::new(MockProviders::default());
        let cache = NowPlayingCache::new(providers);
        let m = MockProviders::default();

        let out = dispatch("now_playing_list", &json!({}), &m, Some(&cache), None)
            .unwrap()
            .unwrap();
        assert_eq!(out["version"], 0);

        cache.bump_list_version();
        let out = dispatch("now_playing_list", &json!({}), &m, Some(&cache), None)
            .unwrap()
            .unwrap();
        assert_eq!(out["version"], 1);
    }

    /// The guard is what makes the version worth carrying: without it a remove
    /// racing a queue change deletes whatever now sits at that index.
    #[test]
    fn a_mutation_against_a_stale_version_is_refused() {
        let providers: std::sync::Arc<dyn Providers> =
            std::sync::Arc::new(MockProviders::default());
        let cache = NowPlayingCache::new(providers);
        cache.bump_list_version(); // the client read version 0, the queue moved
        let m = MockProviders::default();

        let err = dispatch(
            "now_playing_list_remove",
            &json!({ "order": 2, "version": 0 }),
            &m,
            Some(&cache),
            None,
        )
        .unwrap()
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::StaleList);
        assert!(
            !m.recorded()
                .iter()
                .any(|c| c.starts_with("remove_list_item"))
        );
    }

    #[test]
    fn a_mutation_with_the_current_version_runs_and_bumps_it() {
        let providers: std::sync::Arc<dyn Providers> =
            std::sync::Arc::new(MockProviders::default());
        let cache = NowPlayingCache::new(providers);
        let m = MockProviders::default();

        dispatch(
            "now_playing_list_remove",
            &json!({ "order": 2, "version": 0 }),
            &m,
            Some(&cache),
            None,
        )
        .unwrap()
        .unwrap();
        assert!(m.recorded().contains(&"remove_list_item(2)".to_string()));
        // Bumped by the mutation itself: MusicBee's notification is async, and a
        // read in between would otherwise serve a version already spent.
        assert_eq!(cache.list_version(), 1);
    }

    /// A client that sends no version keeps the unguarded behaviour, so the
    /// field is additive rather than a new requirement.
    #[test]
    fn a_mutation_without_a_version_is_not_guarded() {
        let providers: std::sync::Arc<dyn Providers> =
            std::sync::Arc::new(MockProviders::default());
        let cache = NowPlayingCache::new(providers);
        cache.bump_list_version();
        let m = MockProviders::default();

        dispatch(
            "now_playing_list_remove",
            &json!({ "order": 2 }),
            &m,
            Some(&cache),
            None,
        )
        .unwrap()
        .unwrap();
        assert!(m.recorded().contains(&"remove_list_item(2)".to_string()));
    }

    #[test]
    fn unknown_op_is_not_in_this_domain() {
        let m = MockProviders::default();
        assert!(dispatch("player_status", &json!({}), &m, None, None).is_none());
    }

    #[test]
    fn every_advertised_op_dispatches() {
        let m = MockProviders::default();
        let data = json!({ "index": 0, "from": 0, "to": 0, "query": "q", "paths": ["a"] });
        for op in OPS {
            assert!(
                dispatch(op, &data, &m, None, None).is_some(),
                "advertised op {op} is not dispatched"
            );
        }
    }
}
