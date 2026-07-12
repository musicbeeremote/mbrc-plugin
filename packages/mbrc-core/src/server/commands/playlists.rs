//! Playlist handlers: list (paginated) and play by URL.

use serde_json::{json, Value};

use super::{pagination, reply_dto, HandlerResult};
use crate::providers::Providers;

pub fn list(data: &Value, p: &dyn Providers) -> HandlerResult {
    let (offset, limit) = pagination(data);
    reply_dto("playlistlist", &p.playlists(offset, limit)?)
}

pub fn play(data: &Value, p: &dyn Providers) -> HandlerResult {
    p.play_playlist(data.as_str().unwrap_or(""))?;
    Ok(vec![("playlistplay".to_string(), json!(true))])
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::{Page, Playlist};
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
    fn play_acks_and_calls_provider() {
        let m = MockProviders::default();
        assert_eq!(
            play(&json!("playlist://x"), &m).unwrap()[0],
            ("playlistplay".into(), json!(true))
        );
        assert!(m
            .recorded()
            .contains(&"play_playlist(playlist://x)".to_string()));
    }
}
