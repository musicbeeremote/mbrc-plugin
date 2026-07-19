//! V6 playlist domain (V4 parity): list playlists and play one.
//!
//! `playlist_tracks` (#82) and playlist mutations (#115) are feature requests, not
//! V4 features, so they land post-parity.

use serde_json::{json, Value};

use super::{internal, page_args, page_json, req_str, OpResult};
use crate::providers::Providers;

/// The op names this domain serves (advertised in the handshake capabilities).
pub const OPS: &[&str] = &["playlist_list", "playlist_play"];

/// Dispatch a `playlist_*` op. `None` if `op` is not in this domain.
pub fn dispatch(op: &str, data: &Value, p: &dyn Providers) -> Option<OpResult> {
    Some(match op {
        "playlist_list" => list(data, p),
        "playlist_play" => play(data, p),
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

fn play(data: &Value, p: &dyn Providers) -> OpResult {
    p.play_playlist(req_str(data, "url")?).map_err(internal)?;
    Ok(json!({}))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::{Page, Playlist};
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
        let out = dispatch("playlist_list", &json!({}), &m).unwrap().unwrap();
        assert_eq!(out["total"], 1);
        assert_eq!(out["items"][0]["url"], "playlist://x");
        assert_eq!(out["items"][0]["name"], "X");
    }

    #[test]
    fn play_calls_the_provider() {
        let m = MockProviders::default();
        let out = dispatch("playlist_play", &json!({ "url": "playlist://x" }), &m)
            .unwrap()
            .unwrap();
        assert_eq!(out, json!({}));
        assert!(m
            .recorded()
            .contains(&"play_playlist(playlist://x)".to_string()));
    }

    #[test]
    fn play_missing_url_is_missing_field() {
        let m = MockProviders::default();
        let err = dispatch("playlist_play", &json!({}), &m)
            .unwrap()
            .unwrap_err();
        assert_eq!(err.code, mbrc_wire::v6::ErrorCode::MissingField);
    }

    #[test]
    fn unknown_op_is_not_in_this_domain() {
        let m = MockProviders::default();
        assert!(dispatch("library_genres", &json!({}), &m).is_none());
    }

    #[test]
    fn every_advertised_op_dispatches() {
        let m = MockProviders::default();
        for op in OPS {
            assert!(
                dispatch(op, &json!({ "url": "x" }), &m).is_some(),
                "advertised op {op} is not dispatched"
            );
        }
    }
}
