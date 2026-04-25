//! `/api/v1/now_playing` — current playback queue.
//!
//! - `GET /?offset=&limit=` — paginated `Page<NowPlayingTrackDto>`.
//! - `POST /play`           body `{path}`     — jump to a queue entry.
//! - `POST /move`           body `{from, to}` — reorder.
//! - `DELETE /:index`                          — remove entry by index.
//! - `POST /search`         body `{query}`    — search inside the queue.
//!
//! Pagination defaults match the legacy server (`offset=0`, `limit=5000`).

use std::sync::Arc;

use axum::extract::{Path, Query, State};
use axum::http::StatusCode;
use axum::routing::{delete, get, post};
use axum::{Json, Router};
use serde::{Deserialize, Serialize};
use tracing::warn;

use crate::server::NowPlayingTrackDto;
use crate::state::AppState;

use super::error::{ApiError, ApiResult};

pub fn routes() -> Router<Arc<AppState>> {
    Router::new()
        .route("/", get(get_list))
        .route("/play", post(post_play))
        .route("/move", post(post_move))
        .route("/:index", delete(delete_entry))
        .route("/search", post(post_search))
}

#[derive(Deserialize)]
struct Pagination {
    #[serde(default)]
    offset: Option<i32>,
    #[serde(default)]
    limit: Option<i32>,
}

#[derive(Serialize)]
struct Page<T: Serialize> {
    items: Vec<T>,
    offset: i32,
    limit: i32,
    total: i32,
}

async fn get_list(
    State(state): State<Arc<AppState>>,
    Query(p): Query<Pagination>,
) -> ApiResult<Json<Page<NowPlayingTrackDto>>> {
    let offset = p.offset.unwrap_or(0);
    let limit = p.limit.unwrap_or(5000);
    let r = tokio::task::spawn_blocking(move || {
        state.callbacks().query_now_playing_list(offset, limit)
    })
    .await
    .map_err(|e| {
        warn!("NowPlayingList spawn_blocking panicked: {}", e);
        ApiError::internal("now playing query panicked")
    })?
    .map_err(|e| {
        warn!("NowPlayingList query failed: {}", e);
        ApiError::internal("now playing query failed")
    })?;
    let total = offset + r.tracks.len() as i32;
    Ok(Json(Page {
        items: r.tracks,
        offset,
        limit,
        total,
    }))
}

#[derive(Deserialize)]
struct PlayBody {
    path: String,
}

async fn post_play(State(state): State<Arc<AppState>>, Json(body): Json<PlayBody>) -> StatusCode {
    let path = body.path;
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().now_playing_list_play(&path);
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}

#[derive(Deserialize)]
struct MoveBody {
    from: i32,
    to: i32,
}

async fn post_move(State(state): State<Arc<AppState>>, Json(body): Json<MoveBody>) -> StatusCode {
    let MoveBody { from, to } = body;
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().now_playing_list_move(from, to);
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}

async fn delete_entry(
    State(state): State<Arc<AppState>>,
    Path(index): Path<i32>,
) -> StatusCode {
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().now_playing_list_remove(index);
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}

#[derive(Deserialize)]
struct SearchBody {
    query: String,
}

async fn post_search(
    State(state): State<Arc<AppState>>,
    Json(body): Json<SearchBody>,
) -> StatusCode {
    let q = body.query;
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().now_playing_list_search(&q);
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}
