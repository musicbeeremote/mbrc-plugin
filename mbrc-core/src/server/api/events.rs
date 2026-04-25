//! `/api/v1/events` — WebSocket fan-out of player/library broadcasts.
//!
//! Each `BroadcastEvent` from `AppState::event_tx` carries one or more
//! `SocketMessage`s. We translate each one into a v1-shaped JSON frame
//! and send it as a `Text` WebSocket message:
//!
//! ```json
//! { "event": "playerstate", "payload": { … } }
//! ```
//!
//! The `event` string mirrors the legacy `context` constant so callers
//! that already understand the legacy wire can re-use those tokens. The
//! `payload` is exactly the JSON the legacy client would receive in the
//! `data` field — no extra reshaping. Bytes/binary frames are not used.
//!
//! Behaviour:
//! - Inbound frames from the client are read and dropped (keeps the
//!   underlying TCP read side moving so close frames are observed).
//! - On `RecvError::Lagged`, a synthetic `events_lagged` frame is sent
//!   so the client can re-fetch state.
//! - On `RecvError::Closed`, the WS is closed cleanly.

use std::sync::Arc;

use axum::extract::ws::{Message, WebSocket, WebSocketUpgrade};
use axum::extract::State;
use axum::response::IntoResponse;
use axum::routing::get;
use axum::Router;
use serde_json::json;
use tokio::sync::broadcast::error::RecvError;
use tracing::{debug, warn};

use crate::protocol::messages::SocketMessage;
use crate::state::AppState;

pub fn routes() -> Router<Arc<AppState>> {
    Router::new().route("/", get(ws_handler))
}

async fn ws_handler(
    ws: WebSocketUpgrade,
    State(state): State<Arc<AppState>>,
) -> impl IntoResponse {
    ws.on_upgrade(move |socket| handle_socket(socket, state))
}

async fn handle_socket(mut socket: WebSocket, state: Arc<AppState>) {
    let mut rx = state.event_tx().subscribe();
    debug!("WS client subscribed to /api/v1/events");

    loop {
        tokio::select! {
            // Drain client → server frames so close/ping behave normally.
            inbound = socket.recv() => {
                match inbound {
                    Some(Ok(Message::Close(_))) | None => {
                        debug!("WS client disconnected");
                        break;
                    }
                    Some(Ok(_)) => {} // ignore data frames
                    Some(Err(e)) => {
                        debug!("WS recv error: {}", e);
                        break;
                    }
                }
            }
            event = rx.recv() => {
                match event {
                    Ok(broadcast) => {
                        for msg in &broadcast.messages {
                            if let Err(e) = send_event(&mut socket, msg).await {
                                debug!("WS send failed, dropping client: {}", e);
                                return;
                            }
                        }
                    }
                    Err(RecvError::Lagged(n)) => {
                        warn!("WS client lagged {} events, sending events_lagged hint", n);
                        let frame = json!({
                            "event": "events_lagged",
                            "payload": { "missed": n },
                        });
                        let _ = socket.send(Message::Text(frame.to_string())).await;
                    }
                    Err(RecvError::Closed) => {
                        debug!("Broadcast channel closed, ending WS");
                        break;
                    }
                }
            }
        }
    }
}

async fn send_event(socket: &mut WebSocket, msg: &SocketMessage) -> Result<(), axum::Error> {
    let frame = json!({
        "event": msg.context,
        "payload": msg.data,
    });
    socket.send(Message::Text(frame.to_string())).await
}
