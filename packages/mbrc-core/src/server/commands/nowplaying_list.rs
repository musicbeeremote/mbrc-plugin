//! Now-playing-list handlers: paginated list, play/remove/move by index,
//! search, and queue. Mutations echo a small success object; the client
//! typically re-requests the list afterward.

use serde_json::{json, Value};

use super::{as_int_lenient, as_set_string, pagination, Ctx, HandlerResult, Platform};
use crate::providers::Providers;

/// Read an index from either a bare int (or stringified int) or an
/// `{ "index": n }` object. The lenient int coercion matches the C#
/// `TryGetData<int>()`, so an index a client sends as `"5"` still lands.
fn index_of(data: &Value) -> i32 {
    as_int_lenient(data)
        .or_else(|| data.get("index").and_then(as_int_lenient))
        .unwrap_or(0) as i32
}

pub fn list(data: &Value, ctx: &Ctx) -> HandlerResult {
    let (offset, limit) = pagination(data);
    // iOS gets the current-index-anchored "ordered" list; Android the
    // sequential page. The wire codec then shapes the item per platform.
    let page = if ctx.platform == Platform::Ios {
        ctx.providers.now_playing_list_ordered(offset, limit)?
    } else {
        ctx.providers.now_playing_list(offset, limit)?
    };
    Ok(vec![(
        "nowplayinglist".to_string(),
        ctx.wire().now_playing_list(&page, ctx.platform),
    )])
}

pub fn play(data: &Value, ctx: &Ctx) -> HandlerResult {
    let mut index = index_of(data);
    // Android sends 1-based list positions; the shipped plugin subtracts 1.
    if ctx.platform == Platform::Android {
        index -= 1;
    }
    ctx.providers.play_list_item(index)?;
    Ok(vec![("nowplayinglistplay".to_string(), json!(true))])
}

pub fn remove(data: &Value, p: &dyn Providers) -> HandlerResult {
    let index = index_of(data);
    p.remove_list_item(index)?;
    Ok(vec![(
        "nowplayinglistremove".to_string(),
        json!({ "success": true, "index": index }),
    )])
}

pub fn move_track(data: &Value, p: &dyn Providers) -> HandlerResult {
    let from = data.get("from").and_then(as_int_lenient).unwrap_or(0) as i32;
    let to = data.get("to").and_then(as_int_lenient).unwrap_or(0) as i32;
    p.move_list_item(from, to)?;
    Ok(vec![(
        "nowplayinglistmove".to_string(),
        json!({ "success": true, "from": from, "to": to }),
    )])
}

pub fn search(data: &Value, p: &dyn Providers) -> HandlerResult {
    p.search_list(&as_set_string(data).unwrap_or_default())?;
    Ok(vec![("nowplayinglistsearch".to_string(), json!(true))])
}

pub fn queue(data: &Value, ctx: &Ctx) -> HandlerResult {
    let queue_type = ctx
        .wire()
        .parse_queue_type(data.get("queue").and_then(Value::as_str).unwrap_or("next"));
    let play = data.get("play").and_then(Value::as_str).unwrap_or("");
    let files = data
        .get("data")
        .and_then(Value::as_array)
        .map(|items| {
            items
                .iter()
                .filter_map(|v| v.as_str().map(String::from))
                .collect()
        })
        .unwrap_or_default();
    ctx.providers.queue(queue_type, files, play)?;
    Ok(vec![(
        "nowplayingqueue".to_string(),
        json!({ "code": 200 }),
    )])
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::{NowPlayingListTrack, Page};
    use crate::protocol::version::ProtocolVersion;
    use crate::providers::MockProviders;

    fn item() -> NowPlayingListTrack {
        NowPlayingListTrack {
            artist: "Artist".into(),
            album: "Album".into(),
            album_artist: "AlbumArtist".into(),
            title: "Song".into(),
            path: "p".into(),
            position: 3,
        }
    }

    fn one(track: NowPlayingListTrack) -> Page<NowPlayingListTrack> {
        Page {
            offset: 0,
            limit: 100,
            total: 1,
            data: vec![track],
        }
    }

    #[test]
    fn list_replies_page_envelope() {
        let m = MockProviders {
            now_playing_list: one(NowPlayingListTrack {
                title: "Song".into(),
                ..Default::default()
            }),
            ..Default::default()
        };
        let ctx = Ctx::new(&m, ProtocolVersion::V4);
        let out = list(&json!({"offset": 0, "limit": 100}), &ctx).unwrap();
        assert_eq!(out[0].0, "nowplayinglist");
        assert_eq!(out[0].1["total"], json!(1));
        assert_eq!(out[0].1["data"][0]["title"], json!("Song"));
    }

    #[test]
    fn android_omits_album_uses_page_and_1based_index() {
        let m = MockProviders {
            now_playing_list: one(item()),
            ..Default::default()
        };
        let ctx = Ctx::new(&m, ProtocolVersion::V4).with_platform(Platform::Android);
        let out = list(&json!({"offset": 0, "limit": 100}), &ctx).unwrap();
        let track = &out[0].1["data"][0];
        assert!(track.get("album").is_none(), "Android must omit album");
        assert!(track.get("album_artist").is_none());
        assert_eq!(track["artist"], json!("Artist"));
        // Android uses the sequential page (not the ordered query).
        assert!(m.recorded().contains(&"now_playing_list".to_string()));

        // 1-based -> 0-based: client index 3 hits provider index 2.
        play(&json!(3), &ctx).unwrap();
        assert!(m.recorded().contains(&"play_list_item(2)".to_string()));
    }

    #[test]
    fn ios_includes_album_uses_ordered_and_raw_index() {
        let m = MockProviders {
            now_playing_list_ordered: one(item()),
            ..Default::default()
        };
        let ctx = Ctx::new(&m, ProtocolVersion::V4).with_platform(Platform::Ios);
        let out = list(&json!({"offset": 0, "limit": 100}), &ctx).unwrap();
        let track = &out[0].1["data"][0];
        assert_eq!(track["album"], json!("Album"));
        assert_eq!(track["album_artist"], json!("AlbumArtist"));
        assert!(m
            .recorded()
            .contains(&"now_playing_list_ordered".to_string()));

        // iOS index is used as-is.
        play(&json!(3), &ctx).unwrap();
        assert!(m.recorded().contains(&"play_list_item(3)".to_string()));
    }

    #[test]
    fn remove_accepts_int_or_object_and_reports_success() {
        let m = MockProviders::default();
        assert_eq!(
            remove(&json!(5), &m).unwrap()[0].1,
            json!({"success": true, "index": 5})
        );
        assert_eq!(
            remove(&json!({"index": 7}), &m).unwrap()[0].1,
            json!({"success": true, "index": 7})
        );
    }

    #[test]
    fn queue_parses_type_and_files_and_replies_code() {
        let m = MockProviders::default();
        let ctx = Ctx::new(&m, crate::protocol::version::ProtocolVersion::V4);
        let out = queue(
            &json!({"queue": "add-all", "play": null, "data": ["a.mp3", "b.mp3"]}),
            &ctx,
        )
        .unwrap();
        assert_eq!(out[0].1, json!({"code": 200}));
        // "add-all" parsed to the canonical QueueType::AddAndPlay.
        assert!(m.recorded().contains(&"queue(AddAndPlay,2,)".to_string()));
    }
}
