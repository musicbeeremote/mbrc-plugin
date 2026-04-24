//! HTTP mirror API — the `/api/v1/*` surface the embedded webapp
//! consumes. Exposes the same data as the legacy TCP protocol but
//! with REST-shaped endpoints and plain JSON bodies.
//!
//! Conventions:
//! - Bare JSON objects; no legacy `{context, data}` envelope — the
//!   "context" is carried by the URL itself.
//! - `snake_case` field names, matching the existing serde DTOs so
//!   both wire formats share the same types.
//! - Permissive CORS for now (the plugin listens on a private network
//!   interface and the webapp is the intended consumer). A configurable
//!   allowlist is tracked separately in the rollout plan.
//!
//! The v1 schema is the long-lived contract — new endpoints MUST be
//! added, existing endpoints MUST NOT be reshaped without a `/api/v2`.

use std::sync::Arc;

use axum::extract::State;
use axum::routing::get;
use axum::{Json, Router};
use serde::Serialize;
use tower_http::cors::CorsLayer;

use crate::state::AppState;

/// API version shipped by this build of `mbrc_core`. Distinct from
/// `crate::CARGO_PKG_VERSION` — that tracks the binary, this tracks
/// the HTTP contract and only changes when a new `/api/vN` path is
/// introduced.
pub const API_VERSION: &str = "1";

#[derive(Serialize)]
struct VersionResponse {
    api_version: &'static str,
    core_version: &'static str,
}

async fn version_handler(State(_state): State<Arc<AppState>>) -> Json<VersionResponse> {
    Json(VersionResponse {
        api_version: API_VERSION,
        core_version: env!("CARGO_PKG_VERSION"),
    })
}

/// Build the `/api/v1` sub-router. Nest this under the top-level
/// Axum router so every handler lives behind the versioned prefix.
pub fn router(state: Arc<AppState>) -> Router {
    Router::new()
        .route("/version", get(version_handler))
        .layer(CorsLayer::permissive())
        .with_state(state)
}
