//! `/api/v1/outputs` — audio output device list and switching.
//!
//! - `GET /`         — `{active, devices: […]}` (OutputDevices).
//! - `POST /switch`  body `{device}` — switch active output.

use std::sync::Arc;

use axum::extract::State;
use axum::http::StatusCode;
use axum::routing::{get, post};
use axum::{Json, Router};
use serde::Deserialize;
use tracing::warn;

use crate::server::OutputDevicesResponse;
use crate::state::AppState;

use super::error::{ApiError, ApiResult};

pub fn routes() -> Router<Arc<AppState>> {
    Router::new()
        .route("/", get(get_outputs))
        .route("/switch", post(post_switch))
}

async fn get_outputs(
    State(state): State<Arc<AppState>>,
) -> ApiResult<Json<OutputDevicesResponse>> {
    let r = tokio::task::spawn_blocking(move || state.callbacks().query_output_devices())
        .await
        .map_err(|e| {
            warn!("OutputDevices spawn_blocking panicked: {}", e);
            ApiError::internal("output query panicked")
        })?
        .map_err(|e| {
            warn!("OutputDevices query failed: {}", e);
            ApiError::internal("output query failed")
        })?;
    Ok(Json(r))
}

#[derive(Deserialize)]
struct SwitchBody {
    device: String,
}

async fn post_switch(
    State(state): State<Arc<AppState>>,
    Json(body): Json<SwitchBody>,
) -> StatusCode {
    let device = body.device;
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().output_switch(&device);
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}
