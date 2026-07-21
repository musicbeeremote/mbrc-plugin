//! V6 now-playing LIST domain: the play queue. Per #118 §7 this is ONE canonical
//! list - no `client_type` fork (the V4 ordered/sequential + album-drop quirks
//! dissolve). Two views via `up_next`: the default FULL list in list order (keeps
//! already-played) and the shuffle-aware play-order-from-current view. Each item
//! carries `order` (absolute storage index = the mutation key), `position` (window
//! display rank), and `play_position` (shuffle rank, -1 = already played). Plus
//! play / remove / move / search and queueing - mutations key on `order`
//! (versioned-order #110 is post-parity).

use std::collections::HashMap;

use serde_json::{json, Value};

use super::{
    i32_saturating, internal, opt_bool, page_args, page_json, req_i64, req_str, req_str_array,
    track, OpResult,
};
use crate::cover::store::CoverStore;
use crate::protocol::messages::{NowPlayingListTrack, QueueType, TrackTags};
use crate::providers::Providers;

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
    cover_store: Option<&CoverStore>,
) -> Option<OpResult> {
    Some(match op {
        "now_playing_list" => list(data, p, cover_store),
        "now_playing_list_play" => play(data, p),
        "now_playing_list_remove" => remove(data, p),
        "now_playing_list_move" => move_item(data, p),
        "now_playing_list_search" => search(data, p),
        "now_playing_queue" => queue(data, p),
        _ => return None,
    })
}

/// The now-playing queue as a `Page` of canonical tracks. Two views, selected by
/// `up_next`, and each item carries three indices:
///
/// - **`order`** - the absolute 0-based MusicBee list index. This IS the key the
///   mutations consume (`now_playing_list_play`/`remove`/`move`), so `order` ==
///   the index you pass back. Contiguous in the default view; non-contiguous in
///   the up-next view (it follows shuffle).
/// - **`position`** - the 0-based sequential rank within the presented window
///   (contiguous: `offset`, `offset+1`, ...). The display sequence.
/// - **`play_position`** - the 0-based rank in the shuffle PLAY order (0 = current,
///   1 = next up, ...), or **-1 if the track has already been played**. Lets the
///   default (list-order) view convey play order + played state without the
///   client re-deriving it.
///
/// Views:
/// - **default (`up_next` false/absent):** the FULL list in list order - every
///   track, played and unplayed, exactly as MusicBee holds it. `order` ==
///   `position` == the storage index; `play_position` marks play order / played.
/// - **`up_next: true`:** MusicBee's shuffle-aware **play order from the current
///   track** (the former iOS "ordered" view); already-played tracks are dropped.
///   `order` is the true storage index (mutation key), `position` == `play_position`.
///
/// (A single view that is BOTH play order AND keeps already-played is impossible:
/// `GetNextIndex` is forward-only, so played tracks' play order is unrecoverable -
/// hence `play_position: -1` for them rather than a reordered full list.)
fn list(data: &Value, p: &dyn Providers, store: Option<&CoverStore>) -> OpResult {
    let (offset, limit) = page_args(data)?;
    let up_next = opt_bool(data, "up_next")?.unwrap_or(false);
    let page = if up_next {
        p.now_playing_list_ordered(offset as i32, limit as i32)
    } else {
        p.now_playing_list(offset as i32, limit as i32)
    }
    .map_err(internal)?;

    // In the default (full list-order) view, annotate each track with its rank in
    // the shuffle PLAY order (`play_position`): 0 = current, 1 = next up, ..., and
    // -1 for an already-played track. MusicBee's `GetNextIndex` is forward-only,
    // so we can identify played tracks (absent from the forward walk) but not
    // recover their play order - hence the -1 sentinel. The up-next view already
    // IS the play order, so its `play_position` == the window rank.
    let play_rank: HashMap<i32, i64> = if up_next {
        HashMap::new()
    } else {
        // The forward walk carries the storage index in `position`; its ordinal
        // is the play rank. (Indices only; small queues, so the tag reads here are
        // acceptable - a lightweight indices-only provider is a future tidy-up.)
        p.now_playing_list_ordered(0, 0)
            .map_err(internal)?
            .data
            .iter()
            .enumerate()
            .map(|(rank, npt)| (npt.position, rank as i64))
            .collect()
    };

    // One batch tag read for the window's paths; a path not in the library (a
    // queued external file) is unresolved and falls back to its basic fields.
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
            let rank = offset + i as i64; // sequential display rank (0-based)
                                          // The mutation key = the absolute storage index. In the up-next view
                                          // the provider carries it in `position` (the shuffle walk); in the
                                          // full list-order view the sequential rank IS the storage index.
            let order = if up_next { npt.position as i64 } else { rank };
            // Shuffle play rank; up-next items are the play order (rank == window
            // rank), default items look it up (-1 = already played).
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
    Ok(page_json(page.total.max(0) as usize, offset, items))
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

fn play(data: &Value, p: &dyn Providers) -> OpResult {
    p.play_list_item(i32_saturating(req_i64(data, "index")?))
        .map_err(internal)?;
    Ok(json!({}))
}

fn remove(data: &Value, p: &dyn Providers) -> OpResult {
    p.remove_list_item(i32_saturating(req_i64(data, "index")?))
        .map_err(internal)?;
    Ok(json!({}))
}

fn move_item(data: &Value, p: &dyn Providers) -> OpResult {
    let from = i32_saturating(req_i64(data, "from")?);
    let to = i32_saturating(req_i64(data, "to")?);
    p.move_list_item(from, to).map_err(internal)?;
    Ok(json!({}))
}

fn search(data: &Value, p: &dyn Providers) -> OpResult {
    p.search_list(req_str(data, "query")?).map_err(internal)?;
    Ok(json!({}))
}

fn queue(data: &Value, p: &dyn Providers) -> OpResult {
    let paths = req_str_array(data, "paths")?;
    let mode = data.get("mode").and_then(Value::as_str).unwrap_or("next");
    let play = data.get("play").and_then(Value::as_str).unwrap_or("");
    p.queue(parse_queue_type(mode), paths, play)
        .map_err(internal)?;
    Ok(json!({}))
}

/// Map the V6 `mode` string to a queue placement (`next` is the default).
fn parse_queue_type(mode: &str) -> QueueType {
    match mode {
        "last" => QueueType::Last,
        "now" => QueueType::PlayNow,
        "add-all" => QueueType::AddAndPlay,
        _ => QueueType::Next,
    }
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
        let out = dispatch("now_playing_list", &json!({}), &m, None)
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
        let out = dispatch("now_playing_list", &json!({ "up_next": true }), &m, None)
            .unwrap()
            .unwrap();
        assert!(m
            .recorded()
            .contains(&"now_playing_list_ordered".to_string()));
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

    #[test]
    fn play_remove_move_call_providers() {
        let m = MockProviders::default();
        dispatch("now_playing_list_play", &json!({ "index": 3 }), &m, None)
            .unwrap()
            .unwrap();
        dispatch("now_playing_list_remove", &json!({ "index": 2 }), &m, None)
            .unwrap()
            .unwrap();
        dispatch(
            "now_playing_list_move",
            &json!({ "from": 1, "to": 4 }),
            &m,
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
        )
        .unwrap()
        .unwrap();
        assert!(m.recorded().iter().any(|c| c.starts_with("queue")));
    }

    #[test]
    fn queue_missing_paths_is_missing_field() {
        let m = MockProviders::default();
        let err = dispatch("now_playing_queue", &json!({}), &m, None)
            .unwrap()
            .unwrap_err();
        assert_eq!(err.code, mbrc_wire::v6::ErrorCode::MissingField);
    }

    #[test]
    fn play_bad_index_is_invalid_or_missing() {
        let m = MockProviders::default();
        let err = dispatch("now_playing_list_play", &json!({}), &m, None)
            .unwrap()
            .unwrap_err();
        assert_eq!(err.code, mbrc_wire::v6::ErrorCode::MissingField);
    }

    #[test]
    fn parse_queue_type_maps_modes() {
        assert!(matches!(parse_queue_type("last"), QueueType::Last));
        assert!(matches!(parse_queue_type("now"), QueueType::PlayNow));
        assert!(matches!(parse_queue_type("add-all"), QueueType::AddAndPlay));
        assert!(matches!(parse_queue_type("next"), QueueType::Next));
        assert!(matches!(parse_queue_type("wat"), QueueType::Next));
    }

    #[test]
    fn unknown_op_is_not_in_this_domain() {
        let m = MockProviders::default();
        assert!(dispatch("player_status", &json!({}), &m, None).is_none());
    }

    #[test]
    fn every_advertised_op_dispatches() {
        let m = MockProviders::default();
        let data = json!({ "index": 0, "from": 0, "to": 0, "query": "q", "paths": ["a"] });
        for op in OPS {
            assert!(
                dispatch(op, &data, &m, None).is_some(),
                "advertised op {op} is not dispatched"
            );
        }
    }
}
