//! `/api/v1/track` — now-playing track info + seek.
//!
//! - `GET /` — `TrackInfoResponse` (artist/title/album/year/path).
//! - `GET /cover` — raw image bytes. C# delivers a base64 string over the
//!   FFI; we decode it and respond with the appropriate `Content-Type`
//!   (sniffed from the magic bytes — JPEG or PNG covers virtually every
//!   case MusicBee produces). Empty cover → 404.
//! - `GET /lyrics` — `{lyrics: "…"}` (same wrapping rationale).
//! - `GET /details` — extended tag block (`NowPlayingDetailsResponse`).
//! - `GET /position` — `{current, total}` in milliseconds.
//! - `PUT /position` body `{value: <ms>}` — seek; 204 on accept.

use std::sync::Arc;

use axum::extract::State;
use axum::http::{header, StatusCode};
use axum::response::{IntoResponse, Response};
use axum::routing::get;
use axum::{Json, Router};
use base64::Engine as _;
use serde::{Deserialize, Serialize};
use tracing::warn;

use crate::ffi::types::QueryType;
use crate::server::{NowPlayingDetailsResponse, PlaybackPositionResponse, TrackInfoResponse};
use crate::state::AppState;

use super::error::{ApiError, ApiResult};

pub fn routes() -> Router<Arc<AppState>> {
    Router::new()
        .route("/", get(get_track))
        .route("/cover", get(get_cover))
        .route("/lyrics", get(get_lyrics))
        .route("/details", get(get_details))
        .route("/position", get(get_position).put(put_position))
}

#[derive(Deserialize)]
struct IntValue {
    value: i32,
}

#[derive(Serialize)]
struct LyricsResponse {
    lyrics: String,
}

async fn query_blocking<R, F>(label: &'static str, f: F) -> ApiResult<R>
where
    R: Send + 'static,
    F: FnOnce() -> Result<R, String> + Send + 'static,
{
    tokio::task::spawn_blocking(f)
        .await
        .map_err(|e| {
            warn!("{} spawn_blocking panicked: {}", label, e);
            ApiError::internal(format!("{} query panicked", label))
        })?
        .map_err(|e| {
            warn!("{} query failed: {}", label, e);
            ApiError::internal(format!("{} query failed", label))
        })
}

async fn get_track(State(state): State<Arc<AppState>>) -> ApiResult<Json<TrackInfoResponse>> {
    let track = query_blocking("TrackInfo", move || {
        state
            .callbacks()
            .query_no_params::<TrackInfoResponse>(QueryType::TrackInfo)
            .map_err(|e| e.to_string())
    })
    .await?;
    Ok(Json(track))
}

async fn get_cover(State(state): State<Arc<AppState>>) -> ApiResult<Response> {
    let b64 = query_blocking("CoverData", move || {
        state
            .callbacks()
            .query_no_params::<String>(QueryType::CoverData)
            .map_err(|e| e.to_string())
    })
    .await?;

    if b64.is_empty() {
        return Err(ApiError::not_found("no cover available"));
    }

    let bytes = base64::engine::general_purpose::STANDARD
        .decode(b64.as_bytes())
        .map_err(|e| {
            warn!("Cover base64 decode failed: {}", e);
            ApiError::internal("cover decode failed")
        })?;

    let content_type = sniff_image(&bytes).unwrap_or("application/octet-stream");
    Ok(([(header::CONTENT_TYPE, content_type)], bytes).into_response())
}

fn sniff_image(bytes: &[u8]) -> Option<&'static str> {
    if bytes.starts_with(&[0xFF, 0xD8, 0xFF]) {
        Some("image/jpeg")
    } else if bytes.starts_with(&[0x89, 0x50, 0x4E, 0x47]) {
        Some("image/png")
    } else if bytes.starts_with(b"GIF8") {
        Some("image/gif")
    } else if bytes.starts_with(b"RIFF") && bytes.len() >= 12 && &bytes[8..12] == b"WEBP" {
        Some("image/webp")
    } else {
        None
    }
}

async fn get_lyrics(State(state): State<Arc<AppState>>) -> ApiResult<Json<LyricsResponse>> {
    let lyrics = query_blocking("Lyrics", move || {
        state
            .callbacks()
            .query_no_params::<String>(QueryType::Lyrics)
            .map_err(|e| e.to_string())
    })
    .await?;
    Ok(Json(LyricsResponse { lyrics }))
}

async fn get_details(
    State(state): State<Arc<AppState>>,
) -> ApiResult<Json<NowPlayingDetailsResponse>> {
    let details = query_blocking("NowPlayingDetails", move || {
        state
            .callbacks()
            .query_no_params::<NowPlayingDetailsResponse>(QueryType::NowPlayingDetails)
            .map_err(|e| e.to_string())
    })
    .await?;
    Ok(Json(details))
}

async fn get_position(
    State(state): State<Arc<AppState>>,
) -> ApiResult<Json<PlaybackPositionResponse>> {
    let pos = query_blocking("PlaybackPosition", move || {
        state
            .callbacks()
            .query_no_params::<PlaybackPositionResponse>(QueryType::PlaybackPosition)
            .map_err(|e| e.to_string())
    })
    .await?;
    Ok(Json(pos))
}

async fn put_position(
    State(state): State<Arc<AppState>>,
    Json(body): Json<IntValue>,
) -> StatusCode {
    let value = body.value;
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().player_set_position(value);
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}
