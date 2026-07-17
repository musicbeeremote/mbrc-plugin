//! Now-playing track handlers: track info, details, position, cover, lyrics,
//! rating, Last.fm rating, and tag changes. The response DTOs already match the
//! wire shapes, so most handlers just serialize the provider result.

use serde_json::{json, Value};

use super::{as_int_lenient, as_set_string, Ctx, HandlerResult};
use crate::protocol::messages::LastfmStatus;
use crate::providers::Providers;

/// Serialize a DTO onto a context as its reply frame.
fn frame_dto<T: serde::Serialize>(context: &str, dto: &T) -> HandlerResult {
    let data = serde_json::to_value(dto).map_err(|e| e.to_string())?;
    Ok(vec![(context.to_string(), data)])
}

pub fn track_info(ctx: &Ctx) -> HandlerResult {
    let info = ctx.now_track_info()?;
    Ok(vec![(
        "nowplayingtrack".to_string(),
        ctx.wire().track_info(&info),
    )])
}

pub fn details(ctx: &Ctx) -> HandlerResult {
    let details = ctx.now_track_details()?;
    Ok(vec![(
        "nowplayingdetails".to_string(),
        ctx.wire().track_details(&details),
    )])
}

/// Seek (int ms) or query (null/`"status"`); reply the current `{current,total}`.
/// A stringified `"120000"` is coerced to an int (C# `TryGetData<int>()` did),
/// while the iOS `"status"` poll and `null` stay queries.
pub fn position(data: &Value, p: &dyn Providers) -> HandlerResult {
    if let Some(ms) = as_int_lenient(data) {
        p.set_position(ms as i32)?;
    }
    frame_dto("nowplayingposition", &p.playback_position()?)
}

/// V5 iOS `nowplayingcurrentposition` alias: report the current `{current,total}`
/// on the `nowplayingposition` context. Query-only - unlike `position` it never
/// seeks, matching the C# `RequestCurrentPosition`, which ignores the payload and
/// always reports.
pub fn current_position(p: &dyn Providers) -> HandlerResult {
    frame_dto("nowplayingposition", &p.playback_position()?)
}

/// Now-playing cover on request. The C# host hands over the raw MusicBee
/// artwork; the core owns sizing and resizes it to the 600px now-playing
/// default (matching the shipped plugin). On any resize failure we log and send
/// the original bytes rather than dropping the cover.
pub fn cover(ctx: &Ctx) -> HandlerResult {
    const NOW_PLAYING_COVER_MAX: u32 = 600;
    let mut c = ctx.now_cover()?;
    if c.status == 200 && !c.cover.is_empty() {
        match crate::cover::resize_base64_jpeg(
            &c.cover,
            NOW_PLAYING_COVER_MAX,
            NOW_PLAYING_COVER_MAX,
        ) {
            Ok(resized) => c.cover = resized,
            Err(e) => tracing::warn!(error = %e, "cover resize failed; sending original"),
        }
    }
    frame_dto("nowplayingcover", &c)
}

pub fn lyrics(ctx: &Ctx) -> HandlerResult {
    let lyrics = ctx.now_lyrics()?;
    Ok(vec![(
        "nowplayinglyrics".to_string(),
        ctx.wire().lyrics(&lyrics),
    )])
}

/// Set (`"0"`-`"5"` or `""` to clear) or query (null); reply the rating string.
/// A set mutates MusicBee, so its reply reads back fresh from the provider; a
/// pure query serves from the cache.
///
/// Android sends the new rating as a string (`"5"`); iOS sends it as a bare
/// number (`5`). Accept both - if only strings were treated as a set, an iOS
/// rating would fall through to the query branch and be silently swallowed (the
/// rating never changes and the client sees the stale value).
pub fn rating(data: &Value, ctx: &Ctx) -> HandlerResult {
    let value = if let Some(r) = as_set_string(data) {
        ctx.providers.set_rating(&r)?;
        ctx.providers.rating()?
    } else {
        ctx.now_rating()?
    };
    Ok(vec![("nowplayingrating".to_string(), json!(value))])
}

/// Toggle/love/ban or query; reply the current Last.fm status. Mirrors the C#
/// handler: `toggle` flips to Normal from Love/Ban (else to Love); `love`/`ban`
/// set directly; anything else is a query.
pub fn lfm_rating(data: &Value, ctx: &Ctx) -> HandlerResult {
    let p = ctx.providers;
    let status = if let Some(action) = data.as_str() {
        // A mutation reads current state fresh, applies, and replies fresh.
        if action.eq_ignore_ascii_case("toggle") {
            let next = match p.lfm_rating()? {
                LastfmStatus::Love | LastfmStatus::Ban => LastfmStatus::Normal,
                LastfmStatus::Normal => LastfmStatus::Love,
            };
            p.set_lfm_rating(next)?;
        } else if let Some(status) = ctx.wire().parse_lfm(action) {
            p.set_lfm_rating(status)?;
        }
        p.lfm_rating()?
    } else {
        // A pure query serves from the cache.
        ctx.now_lfm()?
    };
    Ok(vec![(
        "nowplayinglfmrating".to_string(),
        ctx.wire().lfm_status(status),
    )])
}

/// Change a metadata tag on the current track; reply the updated details on the
/// `nowplayingdetails` context (matching the C# handler).
pub fn tag_change(data: &Value, p: &dyn Providers) -> HandlerResult {
    let tag = data.get("tag").and_then(Value::as_str).unwrap_or("");
    let value = data.get("value").and_then(Value::as_str).unwrap_or("");
    if tag.is_empty() {
        return Err("tagchange missing 'tag'".to_string());
    }
    p.set_tag(tag, value)?;
    frame_dto("nowplayingdetails", &p.track_details()?)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::{Cover, LastfmStatus, TrackInfo};
    use crate::protocol::version::ProtocolVersion;
    use crate::providers::MockProviders;

    #[test]
    fn track_info_serializes_wire_shape() {
        let m = MockProviders {
            track_info: TrackInfo {
                artist: "Artist".into(),
                title: "Song".into(),
                album: "Album".into(),
                year: "2024".into(),
                path: "C:\\m.mp3".into(),
            },
            ..Default::default()
        };
        let out = track_info(&Ctx::new(&m, ProtocolVersion::V4)).unwrap();
        assert_eq!(out[0].0, "nowplayingtrack");
        assert_eq!(out[0].1["artist"], json!("Artist"));
        assert_eq!(out[0].1["path"], json!("C:\\m.mp3"));
    }

    #[test]
    fn cover_omits_field_when_empty() {
        let m = MockProviders {
            cover: Cover {
                status: 404,
                cover: String::new(),
            },
            ..Default::default()
        };
        let out = cover(&Ctx::new(&m, ProtocolVersion::V4)).unwrap();
        assert_eq!(out[0].1, json!({ "status": 404 })); // no "cover" key
    }

    #[test]
    fn position_seeks_on_int_then_replies() {
        let m = MockProviders::default();
        let out = position(&json!(120000), &m).unwrap();
        assert_eq!(out[0].0, "nowplayingposition");
        assert!(m.recorded().contains(&"set_position(120000)".to_string()));
        assert!(m.recorded().contains(&"playback_position".to_string()));
    }

    #[test]
    fn current_position_replies_on_position_context_without_seeking() {
        // The V5 alias is query-only: it reports on `nowplayingposition` and
        // never calls set_position, unlike the seek-or-query `position`.
        let m = MockProviders::default();
        let out = current_position(&m).unwrap();
        assert_eq!(out[0].0, "nowplayingposition");
        assert!(m.recorded().contains(&"playback_position".to_string()));
        assert!(!m.recorded().iter().any(|c| c.starts_with("set_position")));
    }

    #[test]
    fn rating_sets_then_replies_string() {
        let m = MockProviders {
            rating: "4".into(),
            ..Default::default()
        };
        let out = rating(&json!("4"), &Ctx::new(&m, ProtocolVersion::V4)).unwrap();
        assert_eq!(out[0], ("nowplayingrating".into(), json!("4")));
        assert!(m.recorded().contains(&"set_rating(4)".to_string()));
    }

    #[test]
    fn rating_accepts_ios_numeric_value_as_a_set() {
        // iOS sends the rating as a bare number, not a string. It must still be
        // treated as a set (call set_rating), not fall through to a query.
        let m = MockProviders {
            rating: "5".into(),
            ..Default::default()
        };
        let out = rating(&json!(5), &Ctx::new(&m, ProtocolVersion::V4)).unwrap();
        assert_eq!(out[0], ("nowplayingrating".into(), json!("5")));
        assert!(m.recorded().contains(&"set_rating(5)".to_string()));
    }

    #[test]
    fn rating_null_is_a_query_not_a_set() {
        let m = MockProviders {
            rating: "3".into(),
            ..Default::default()
        };
        let out = rating(&Value::Null, &Ctx::new(&m, ProtocolVersion::V4)).unwrap();
        assert_eq!(out[0].0, "nowplayingrating");
        assert!(!m.recorded().iter().any(|c| c.starts_with("set_rating")));
    }

    #[test]
    fn lfm_rating_sets_then_replies_wire_string() {
        let m = MockProviders {
            lfm_rating: LastfmStatus::Love,
            ..Default::default()
        };
        let ctx = Ctx::new(&m, ProtocolVersion::V4);
        let out = lfm_rating(&json!("love"), &ctx).unwrap();
        assert_eq!(out[0], ("nowplayinglfmrating".into(), json!("Love")));
        // "love" parsed to the canonical LastfmStatus::Love before the FFI call.
        assert!(m.recorded().contains(&"set_lfm_rating(Love)".to_string()));
    }

    #[test]
    fn tag_change_requires_tag_and_replies_details() {
        let m = MockProviders::default();
        assert!(tag_change(&json!({"value": "x"}), &m).is_err());
        let out = tag_change(&json!({"tag": "artist", "value": "New"}), &m).unwrap();
        assert_eq!(out[0].0, "nowplayingdetails");
        assert!(m.recorded().contains(&"set_tag(artist,New)".to_string()));
    }
}
