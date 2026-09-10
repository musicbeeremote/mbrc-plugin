//! `GET /ws` - a V6 session over a WebSocket.
//!
//! Once upgraded the browser is an ordinary V6 client: its first text message is
//! the handshake frame, and every message after it goes to
//! [`V6Session::handle_frame`] unaltered. Events arrive the same way they reach a
//! TCP client, through [`Broadcaster`](crate::server::broadcaster::Broadcaster),
//! whose frames are already JSON lines.

use std::collections::HashMap;

use axum::extract::ws::{Message, WebSocket, WebSocketUpgrade};
use axum::extract::{Query, State};
use axum::response::Response;
use tokio::sync::mpsc;

use crate::server::session_v6::V6Session;
use crate::web::router::WebState;

/// The token rides in the query string, not a header: a browser's `WebSocket`
/// constructor takes a URL and nothing else, so there is no request to attach an
/// `Authorization` header to. The URL never leaves the LAN and the connection is
/// upgraded immediately, so the usual objection to secrets in a URL - proxy and
/// referrer logs - has nowhere to bite here.
pub async fn upgrade(
    State(state): State<WebState>,
    Query(params): Query<HashMap<String, String>>,
    headers: axum::http::HeaderMap,
    ws: WebSocketUpgrade,
) -> Response {
    if !state.admits_request(&headers, params.get("token").map(String::as_str)) {
        return crate::web::router::unauthorized();
    }
    ws.on_upgrade(move |socket| run(socket, state))
}

async fn run(mut socket: WebSocket, state: WebState) {
    let core = state.core;
    let peer = state.peer;
    let conn_id = core.next_conn_id();
    let (out_tx, mut out_rx) = mpsc::unbounded_channel::<String>();

    let mut session = V6Session::default();
    let mut subscribed = false;

    tracing::debug!(%peer, conn_id, "websocket opened");
    loop {
        tokio::select! {
            outbound = out_rx.recv() => match outbound {
                Some(frame) => {
                    if socket.send(Message::Text(frame.into())).await.is_err() {
                        break;
                    }
                }
                None => break,
            },
            inbound = socket.recv() => {
                let Some(Ok(message)) = inbound else { break };
                let Message::Text(line) = message else { continue };
                let outcome = session.handle_frame(
                    &line,
                    core.providers.as_ref(),
                    Some(&core.now_playing),
                    Some(core.cover_store.as_ref()),
                    Some(core.metadata_cache.as_ref()),
                    Some(core.clients.as_ref()),
                );
                for frame in &outcome.replies {
                    if socket.send(Message::Text(frame.clone().into())).await.is_err() {
                        break;
                    }
                }
                if !subscribed && session.reg_meta().is_some_and(|m| m.is_main) {
                    core.v6_broadcaster.register(conn_id, out_tx.clone());
                    subscribed = true;
                }
                if outcome.close {
                    break;
                }
            }
        }
    }

    if subscribed {
        core.v6_broadcaster.unregister(conn_id);
    }
    tracing::debug!(%peer, conn_id, "websocket closed");
}
