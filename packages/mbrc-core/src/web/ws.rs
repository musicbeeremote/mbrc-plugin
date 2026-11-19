//! `GET /ws` - a V6 session over a WebSocket.
//!
//! Once upgraded the browser is an ordinary V6 client: its first text message is
//! the handshake frame, and every message after it goes to
//! [`V6Session::handle_frame`] unaltered. Events arrive the same way they reach a
//! TCP client, through [`Broadcaster`](crate::server::broadcaster::Broadcaster),
//! whose frames are already JSON lines.
//!
//! It is reaped as a TCP V6 socket is: one that never handshakes within the
//! un-handshaked window, or falls silent for three ping intervals, is closed.

use axum::extract::State;
use axum::extract::ws::{Message, WebSocket, WebSocketUpgrade};
use axum::response::Response;
use tokio::sync::mpsc;

use crate::server::connection::IdleTimeouts;
use crate::server::permissions::Principal;
use crate::server::session_v6::V6Session;
use crate::web::router::WebState;

/// A browser's `WebSocket` constructor cannot set a header, so its token is the
/// cookie it sends by itself. A native client can send `Authorization` instead.
pub async fn upgrade(
    State(state): State<WebState>,
    headers: axum::http::HeaderMap,
    ws: WebSocketUpgrade,
) -> Response {
    if !state.admits_request(&headers) {
        return crate::web::router::unauthorized();
    }
    let principal = state.principal(&headers);
    ws.on_upgrade(move |socket| run(socket, state, principal))
}

async fn run(mut socket: WebSocket, state: WebState, principal: Principal) {
    let core = state.core;
    let peer = state.peer;
    let conn_id = core.next_conn_id();
    let (out_tx, mut out_rx) = mpsc::unbounded_channel::<String>();

    let mut session = V6Session::default()
        .with_party_mode(&core.party_mode)
        .with_pairing(&core.pairing, peer.ip())
        .with_power(&core.power)
        .with_server(&core.server)
        .with_ping_interval(core.config.ping_interval_secs)
        .with_principal(principal);
    let mut subscribed = false;
    let timeouts = IdleTimeouts::from(&core.config);
    let mut deadline = tokio::time::Instant::now() + timeouts.unhandshaked;

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
            () = tokio::time::sleep_until(deadline) => {
                tracing::debug!(%peer, conn_id, "websocket silent too long; closing");
                break;
            }
            inbound = socket.recv() => {
                let Some(Ok(message)) = inbound else { break };
                let Message::Text(line) = message else { continue };
                // MusicBee calls block, and may wait on the serial lock behind other clients.
                let outcome = tokio::task::block_in_place(|| {
                    session.handle_frame(
                        &line,
                        core.providers.as_ref(),
                        Some(&core.now_playing),
                        Some(core.cover_store.as_ref()),
                        Some(core.metadata_cache.as_ref()),
                        Some(core.clients.as_ref()),
                    )
                });
                for frame in outcome.replies {
                    if socket.send(Message::Text(frame.into())).await.is_err() {
                        break;
                    }
                }
                if !subscribed && session.reg_meta().is_some_and(|m| m.is_main) {
                    core.v6_broadcaster.register(conn_id, out_tx.clone());
                    if let Some((principal, told)) = session.permissions_watch() {
                        core.party_mode.watch(principal, told, out_tx.clone());
                    }
                    subscribed = true;
                }
                if outcome.close {
                    break;
                }
                let window = if session.is_handshaked() {
                    timeouts.v6_silence
                } else {
                    timeouts.unhandshaked
                };
                deadline = tokio::time::Instant::now() + window;
            }
        }
    }

    if subscribed {
        core.v6_broadcaster.unregister(conn_id);
    }
    tracing::debug!(%peer, conn_id, "websocket closed");
}
