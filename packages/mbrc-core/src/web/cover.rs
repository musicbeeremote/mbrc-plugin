//! `GET /api/cover/{hash}` - album art as an image response.
//! `GET /api/cover/now-playing` - the playing track's own artwork, larger.
//!
//! The one place a resource-shaped GET beats the RPC surface: `<img src>` needs a
//! URL, and a content-addressed one can be cached forever by the browser. The
//! `cover_get` op stays available over the socket for clients that want base64.

use std::collections::HashMap;

use axum::extract::{Path, Query, State};
use axum::http::{StatusCode, header};
use axum::response::{IntoResponse, Response};

use crate::cover::NOW_PLAYING_SIZE;
use crate::web::router::WebState;

/// The hash names the bytes, so a hit can never go stale.
const IMMUTABLE: &str = "public, max-age=31536000, immutable";

/// Without a version to key on, the answer changes with the track.
const NO_CACHE: &str = "no-cache";

pub async fn get(
    State(state): State<WebState>,
    Path(hash): Path<String>,
    Query(params): Query<HashMap<String, String>>,
    headers: axum::http::HeaderMap,
) -> Response {
    if !state.admits_request(&headers, params.get("token").map(String::as_str)) {
        return crate::web::router::unauthorized();
    }
    if !is_content_hash(&hash) {
        return (StatusCode::BAD_REQUEST, "not a content hash").into_response();
    }

    let store = state.core.cover_store.clone();
    let bytes = tokio::task::spawn_blocking(move || store.read_cover_bytes(&hash)).await;

    match bytes {
        Ok(Some(bytes)) => (
            [
                (header::CONTENT_TYPE, "image/jpeg"),
                (header::CACHE_CONTROL, IMMUTABLE),
            ],
            bytes,
        )
            .into_response(),
        Ok(None) => (StatusCode::NOT_FOUND, "no cover for that hash").into_response(),
        Err(e) => {
            tracing::debug!(error = %e, "cover read failed");
            (StatusCode::INTERNAL_SERVER_ERROR, "cover read failed").into_response()
        }
    }
}

/// The playing track's artwork at [`NOW_PLAYING_SIZE`], rendered on demand.
///
/// Not the thumbnail `/api/cover/{hash}` serves: that cache is built at a grid
/// cell's size, so a page showing it large shows an enlargement. The original
/// is one call away, so this renders per request rather than caching a copy.
///
/// `?v=<cover_hash>` is a cache key, not an argument: the response ignores it,
/// and the browser uses it to tell one track's art from the next.
pub async fn now_playing(
    State(state): State<WebState>,
    Query(params): Query<HashMap<String, String>>,
    headers: axum::http::HeaderMap,
) -> Response {
    if !state.admits_request(&headers, params.get("token").map(String::as_str)) {
        return crate::web::router::unauthorized();
    }
    let path = state.core.now_playing.track_info().path;
    if path.is_empty() {
        return (StatusCode::NOT_FOUND, "nothing playing").into_response();
    }

    let core = state.core.clone();
    let rendered = tokio::task::spawn_blocking(move || {
        let raw = crate::cover::from_base64(&core.providers.artwork_raw(&path).ok()?)?;
        crate::cover::resize_to_jpeg(&raw, NOW_PLAYING_SIZE, NOW_PLAYING_SIZE).ok()
    })
    .await;

    let cache = if params.contains_key("v") {
        IMMUTABLE
    } else {
        NO_CACHE
    };
    match rendered {
        Ok(Some(bytes)) => (
            [
                (header::CONTENT_TYPE, "image/jpeg"),
                (header::CACHE_CONTROL, cache),
            ],
            bytes,
        )
            .into_response(),
        Ok(None) => (StatusCode::NOT_FOUND, "no artwork for the playing track").into_response(),
        Err(e) => {
            tracing::debug!(error = %e, "now playing artwork render failed");
            (StatusCode::INTERNAL_SERVER_ERROR, "artwork render failed").into_response()
        }
    }
}

/// Whether the path segment is a lowercase-hex SHA1, the only shape the cover
/// store ever hands out. Rejecting anything else keeps a crafted hash from
/// reaching the store as a path at all.
fn is_content_hash(hash: &str) -> bool {
    hash.len() == 40
        && hash
            .bytes()
            .all(|b| b.is_ascii_hexdigit() && !b.is_ascii_uppercase())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn only_lowercase_hex_sha1_is_a_content_hash() {
        assert!(is_content_hash("da39a3ee5e6b4b0d3255bfef95601890afd80709"));
        assert!(!is_content_hash("DA39A3EE5E6B4B0D3255BFEF95601890AFD80709"));
        assert!(!is_content_hash("da39a3ee"));
        assert!(!is_content_hash(""));
    }

    #[test]
    fn traversal_attempts_are_not_content_hashes() {
        assert!(!is_content_hash("../../../windows/system32/config/sam"));
        assert!(!is_content_hash("da39a3ee5e6b4b0d3255bfef95601890afd8070/"));
    }
}
