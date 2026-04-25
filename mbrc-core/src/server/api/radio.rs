//! `/api/v1/radio` — saved radio stations.
//!
//! - `GET /?offset=&limit=` — paginated `Page<RadioStationDto>`.

use std::sync::Arc;

use axum::extract::{Query, State};
use axum::routing::get;
use axum::{Json, Router};
use serde::{Deserialize, Serialize};
use tracing::warn;

use crate::server::RadioStationDto;
use crate::state::AppState;

use super::error::{ApiError, ApiResult};

pub fn routes() -> Router<Arc<AppState>> {
    Router::new().route("/", get(get_stations))
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

async fn get_stations(
    State(state): State<Arc<AppState>>,
    Query(p): Query<Pagination>,
) -> ApiResult<Json<Page<RadioStationDto>>> {
    let offset = p.offset.unwrap_or(0);
    let limit = p.limit.unwrap_or(5000);
    let r = tokio::task::spawn_blocking(move || {
        state.callbacks().query_radio_stations(offset, limit)
    })
    .await
    .map_err(|e| {
        warn!("RadioStations spawn_blocking panicked: {}", e);
        ApiError::internal("radio query panicked")
    })?
    .map_err(|e| {
        warn!("RadioStations query failed: {}", e);
        ApiError::internal("radio query failed")
    })?;
    let total = offset + r.stations.len() as i32;
    Ok(Json(Page {
        items: r.stations,
        offset,
        limit,
        total,
    }))
}
