//! System handlers dispatched after the handshake: plugin version and `init`
//! (the initial-state bundle). The handshake contexts themselves (player,
//! protocol, ping, pong, verifyconnection) are handled in `session`.

use serde_json::{json, Value};

use super::{reply_dto, Ctx, HandlerResult};
use crate::providers::Providers;

/// The version advertised to V4 legacy clients. The V4 surface is a permanent
/// legacy branch that must present exactly as the last pure-V4 plugin release
/// (1.4.1), independent of the actual build/assembly version - the dev build
/// reports 1.5.0.0, and the iOS 1.4.1 client changes its connection behaviour
/// against a newer version (it stops handshaking its control socket). Pinning
/// this keeps the V4 clients working as before.
pub const V4_PLUGIN_VERSION: &str = "1.4.1.0";

pub fn plugin_version(_p: &dyn Providers) -> HandlerResult {
    Ok(vec![(
        "pluginversion".to_string(),
        json!(V4_PLUGIN_VERSION),
    )])
}

/// The V4 initial-state bundle: to the requesting client the plugin sends the
/// current track, rating, Last.fm rating, and full player status, and pushes
/// the cover and lyrics. In a single request/response exchange all of these go
/// back on this connection. (The cover/lyrics broadcast fan-out is Slice 3; the
/// requester still receives them here.)
pub fn init(ctx: &Ctx) -> HandlerResult {
    // All fields served from the now-playing cache (no FFI on the request path).
    let state = ctx.now_player()?;
    let mut replies: Vec<(String, Value)> = Vec::new();
    replies.extend(reply_dto("nowplayingtrack", &ctx.now_track_info()?)?);
    replies.push(("nowplayingrating".to_string(), json!(ctx.now_rating()?)));
    replies.push((
        "nowplayinglfmrating".to_string(),
        ctx.wire().lfm_status(ctx.now_lfm()?),
    ));
    replies.push(("playerstatus".to_string(), ctx.wire().player_status(&state)));
    // Cover and lyrics match the shipped C# init exactly: pushed only when
    // present. C# sends the `{status:1}` "cover ready" marker only if artwork
    // exists, and lyrics only if non-empty; otherwise it omits them. Emitting
    // `{status:1}` with no artwork wrongly tells the client to fetch a cover
    // that 404s. (init is a state push, so the cover is the marker, not inline.)
    let cover = ctx.now_cover()?;
    if !cover.cover.is_empty() {
        replies.push((
            "nowplayingcover".to_string(),
            ctx.wire().cover_notification(&cover),
        ));
    }
    let lyrics = ctx.now_lyrics()?;
    if !lyrics.lyrics.is_empty() {
        replies.extend(reply_dto("nowplayinglyrics", &lyrics)?);
    }
    Ok(replies)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::providers::MockProviders;

    #[test]
    fn plugin_version_reports_the_v4_legacy_version() {
        // The V4 surface pins the advertised version to 1.4.1.0 regardless of
        // the real build version, so V4 clients keep behaving as before.
        let m = MockProviders {
            plugin_version: "1.5.0.0".into(),
            ..Default::default()
        };
        assert_eq!(
            plugin_version(&m).unwrap()[0],
            ("pluginversion".into(), json!(V4_PLUGIN_VERSION))
        );
    }

    fn mock_with_cover_and_lyrics() -> MockProviders {
        use crate::protocol::messages::{Cover, Lyrics};
        MockProviders {
            cover: Cover {
                status: 200,
                cover: "imgdata".into(),
            },
            lyrics: Lyrics {
                status: 200,
                lyrics: "la la".into(),
            },
            ..Default::default()
        }
    }

    #[test]
    fn init_bundles_the_expected_contexts_in_order() {
        // With artwork and lyrics present, init sends the full 6-frame bundle in
        // C# order.
        let m = mock_with_cover_and_lyrics();
        let ctx = Ctx::new(&m, crate::protocol::version::ProtocolVersion::V4);
        let contexts: Vec<String> = init(&ctx).unwrap().into_iter().map(|(c, _)| c).collect();
        assert_eq!(
            contexts,
            vec![
                "nowplayingtrack",
                "nowplayingrating",
                "nowplayinglfmrating",
                "playerstatus",
                "nowplayingcover",
                "nowplayinglyrics",
            ]
        );
    }

    #[test]
    fn init_cover_is_the_readiness_marker_not_inline() {
        // A state push must send the bare `{status:1}` marker, never the inline
        // image blob (parity with the shipped 1.4.1 plugin).
        let m = mock_with_cover_and_lyrics();
        let ctx = Ctx::new(&m, crate::protocol::version::ProtocolVersion::V4);
        let replies = init(&ctx).unwrap();
        let (_, cover) = replies
            .iter()
            .find(|(c, _)| c == "nowplayingcover")
            .expect("init includes nowplayingcover");
        assert_eq!(*cover, json!({ "status": 1 }));
    }

    #[test]
    fn init_omits_cover_and_lyrics_when_empty() {
        // No artwork / no lyrics: init omits both, exactly like the C# handler
        // (which only broadcasts them when non-empty). Emitting a {status:1}
        // "cover ready" marker with no artwork would make the client 404-fetch.
        let m = MockProviders::default();
        let ctx = Ctx::new(&m, crate::protocol::version::ProtocolVersion::V4);
        let contexts: Vec<String> = init(&ctx).unwrap().into_iter().map(|(c, _)| c).collect();
        assert_eq!(
            contexts,
            vec![
                "nowplayingtrack",
                "nowplayingrating",
                "nowplayinglfmrating",
                "playerstatus",
            ]
        );
    }
}
