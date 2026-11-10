//! Playlist handlers: list (paginated) and play by URL.

use serde_json::{Value, json};

use super::{Ctx, HandlerResult, LIST_PAGE, page_request, reply_dto};
use crate::providers::Providers;
use crate::server::path_guard;

pub fn list(data: &Value, p: &dyn Providers) -> HandlerResult {
    let (offset, limit) = page_request(data, LIST_PAGE);
    reply_dto("playlistlist", &p.playlists(offset, limit)?)
}

pub fn play(data: &Value, ctx: &Ctx) -> HandlerResult {
    let p = ctx.providers;
    let url = data.as_str().unwrap_or("");
    if let Some(unknown) = path_guard::first_refused([url], p, ctx.known_servers()) {
        return Err(format!(
            "refused a system path or unknown server: {unknown}"
        ));
    }
    p.play_playlist(url)?;
    Ok(vec![("playlistplay".to_string(), json!(true))])
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::{Page, Playlist};
    use crate::protocol::version::ProtocolVersion;
    use crate::providers::MockProviders;

    #[test]
    fn list_replies_page_of_playlists() {
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
        let out = list(&json!({"offset":0,"limit":100}), &m).unwrap();
        assert_eq!(out[0].0, "playlistlist");
        assert_eq!(out[0].1["data"][0]["name"], json!("X"));
    }

    #[test]
    fn a_playlist_on_an_unknown_server_is_never_handed_to_the_host() {
        let m = MockProviders::default();
        let ctx = Ctx::new(&m, ProtocolVersion::V4);
        assert!(play(&json!(r"\\attacker\share\x.mbp"), &ctx).is_err());
        assert!(
            !m.recorded()
                .iter()
                .any(|call| call.starts_with("play_playlist")),
            "{:?}",
            m.recorded()
        );
    }

    #[test]
    fn play_acks_and_calls_provider() {
        let m = MockProviders::default();
        let ctx = Ctx::new(&m, ProtocolVersion::V4);
        assert_eq!(
            play(&json!("playlist://x"), &ctx).unwrap()[0],
            ("playlistplay".into(), json!(true))
        );
        assert!(
            m.recorded()
                .contains(&"play_playlist(playlist://x)".to_string())
        );
    }
}
