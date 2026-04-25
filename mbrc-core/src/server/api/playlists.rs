//! `/api/v1/playlists` — list user playlists, trigger playback.
//!
//! - `GET /`         — `{playlists: [{url, name}, ...]}` (PlaylistList).
//! - `POST /play`    body `{url}` — start playback of a saved playlist.

use std::sync::Arc;

use axum::extract::State;
use axum::http::StatusCode;
use axum::routing::{get, post};
use axum::{Json, Router};
use serde::Deserialize;
use tracing::warn;

use crate::server::PlaylistListResponse;
use crate::state::AppState;

use super::error::{ApiError, ApiResult};

pub fn routes() -> Router<Arc<AppState>> {
    Router::new()
        .route("/", get(get_playlists))
        .route("/play", post(post_play))
}

async fn get_playlists(
    State(state): State<Arc<AppState>>,
) -> ApiResult<Json<PlaylistListResponse>> {
    let r = tokio::task::spawn_blocking(move || state.callbacks().query_playlists())
        .await
        .map_err(|e| {
            warn!("PlaylistList spawn_blocking panicked: {}", e);
            ApiError::internal("playlist query panicked")
        })?
        .map_err(|e| {
            warn!("PlaylistList query failed: {}", e);
            ApiError::internal("playlist query failed")
        })?;
    Ok(Json(r))
}

#[derive(Deserialize)]
struct PlayBody {
    url: String,
}

async fn post_play(State(state): State<Arc<AppState>>, Json(body): Json<PlayBody>) -> StatusCode {
    let url = body.url;
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().playlist_play(&url);
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}
