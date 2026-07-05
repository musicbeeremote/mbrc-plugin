//! Tauri command wrapper over the shared `mbrc-discovery` crate.
//!
//! The discovery logic (UDP multicast, interface enumeration, `notify` parsing)
//! lives in `mbrc-discovery` so the CLI shares one implementation. This module
//! is just the async Tauri boundary: it moves the blocking probe onto tokio's
//! blocking pool so the UI thread isn't stalled.

use std::time::Duration;

use mbrc_discovery::{discover_blocking, Discovered};

#[tauri::command]
pub async fn discover(timeout_ms: Option<u64>) -> Result<Vec<Discovered>, String> {
    let timeout = Duration::from_millis(timeout_ms.unwrap_or(3000).clamp(500, 10_000));
    tokio::task::spawn_blocking(move || discover_blocking(timeout))
        .await
        .map_err(|e| e.to_string())?
}
