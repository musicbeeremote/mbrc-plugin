//! `/api/v1/player` — player state + control.
//!
//! - `GET  /` — full `PlayerStateResponse` (snake_case, bare object).
//! - `POST /play|pause|stop|next|previous` — fire-and-forget thin
//!   callbacks; empty 204 response.
//! - `PUT  /volume|mute|repeat|shuffle|scrobble|auto_dj|position` —
//!   request body `{"value": …}`; returns the resulting state (204 for
//!   commands that don't have a readable counterpart in W2).

use std::sync::Arc;

use axum::extract::State;
use axum::http::StatusCode;
use axum::routing::{get, post, put};
use axum::{Json, Router};
use serde::Deserialize;
use tracing::warn;

use crate::ffi::types::QueryType;
use crate::server::PlayerStateResponse;
use crate::state::AppState;

use super::error::{ApiError, ApiResult};

pub fn routes() -> Router<Arc<AppState>> {
    Router::new()
        .route("/", get(get_state))
        .route("/play", post(post_play))
        .route("/pause", post(post_pause))
        .route("/stop", post(post_stop))
        .route("/next", post(post_next))
        .route("/previous", post(post_previous))
        .route("/volume", put(put_volume))
        .route("/mute", put(put_mute))
        .route("/repeat", put(put_repeat))
        .route("/shuffle", put(put_shuffle))
        .route("/scrobble", put(put_scrobble))
        .route("/auto_dj", put(put_auto_dj))
}

#[derive(Deserialize)]
struct BoolValue {
    value: bool,
}

#[derive(Deserialize)]
struct IntValue {
    value: i32,
}

#[derive(Deserialize)]
struct StringValue {
    value: String,
}

// ── GET ────────────────────────────────────────────────────────────

async fn get_state(
    State(state): State<Arc<AppState>>,
) -> ApiResult<Json<PlayerStateResponse>> {
    let ps = tokio::task::spawn_blocking(move || {
        state
            .callbacks()
            .query_no_params::<PlayerStateResponse>(QueryType::PlayerState)
    })
    .await
    .map_err(|e| {
        warn!("spawn_blocking panicked: {}", e);
        ApiError::internal("player state query panicked")
    })?
    .map_err(|e| {
        warn!("PlayerState query failed: {}", e);
        ApiError::internal("player state query failed")
    })?;
    Ok(Json(ps))
}

// ── POST fire-and-forget ───────────────────────────────────────────

async fn post_play(State(state): State<Arc<AppState>>) -> StatusCode {
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().player_play_pause();
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}

async fn post_pause(State(state): State<Arc<AppState>>) -> StatusCode {
    // Legacy TCP reuses play_pause for both — MusicBee only exposes a
    // toggle. Kept as a separate route so HTTP callers don't need to
    // track state; semantics are documented in the rollout plan.
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().player_play_pause();
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}

async fn post_stop(State(state): State<Arc<AppState>>) -> StatusCode {
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().player_stop();
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}

async fn post_next(State(state): State<Arc<AppState>>) -> StatusCode {
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().player_next();
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}

async fn post_previous(State(state): State<Arc<AppState>>) -> StatusCode {
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().player_previous();
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}

// ── PUT setters ────────────────────────────────────────────────────

async fn put_volume(
    State(state): State<Arc<AppState>>,
    Json(body): Json<IntValue>,
) -> StatusCode {
    let value = body.value;
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().player_set_volume(value);
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}

async fn put_mute(
    State(state): State<Arc<AppState>>,
    Json(body): Json<BoolValue>,
) -> StatusCode {
    let value = body.value;
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().set_mute(value);
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}

async fn put_repeat(
    State(state): State<Arc<AppState>>,
    Json(body): Json<StringValue>,
) -> StatusCode {
    let mode = body.value;
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().set_repeat(&mode);
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}

async fn put_shuffle(
    State(state): State<Arc<AppState>>,
    Json(body): Json<BoolValue>,
) -> StatusCode {
    let value = body.value;
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().set_shuffle(value);
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}

async fn put_scrobble(
    State(state): State<Arc<AppState>>,
    Json(body): Json<BoolValue>,
) -> StatusCode {
    let value = body.value;
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().set_scrobble(value);
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}

async fn put_auto_dj(
    State(state): State<Arc<AppState>>,
    Json(body): Json<BoolValue>,
) -> StatusCode {
    let value = body.value;
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().set_auto_dj(value);
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}
