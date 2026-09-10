//! Server-sent events: the broadcast half of the protocol for a browser whose
//! WebSocket will not open.
//!
//! Commands already survive that - they fall back to `POST /api/v6/{op}` - but
//! events had no second road at all, so such a session read the state once and
//! then watched it go stale. This is one-way and needs no handshake: it carries
//! exactly what the V6 broadcaster fans out, in the same envelopes, so a client
//! feeds the lines it receives to the same reader the socket's messages go to.

use std::collections::HashMap;
use std::convert::Infallible;
use std::pin::Pin;
use std::sync::Arc;
use std::task::{Context, Poll};

use axum::extract::{Query, State};
use axum::response::sse::{Event, KeepAlive, Sse};
use axum::response::{IntoResponse, Response};
use tokio::sync::mpsc;

use crate::state::Core;
use crate::web::router::WebState;

/// The token rides in the query string for the same reason it does on the
/// WebSocket: `EventSource` takes a URL and nothing else.
pub async fn stream(
    State(state): State<WebState>,
    Query(params): Query<HashMap<String, String>>,
    headers: axum::http::HeaderMap,
) -> Response {
    if !state.admits_request(&headers, params.get("token").map(String::as_str)) {
        return crate::web::router::unauthorized();
    }

    let core = state.core;
    let conn_id = core.next_conn_id();
    let (tx, rx) = mpsc::unbounded_channel();
    core.v6_broadcaster.register(conn_id, tx);
    tracing::debug!(peer = %state.peer, conn_id, "event stream opened");

    Sse::new(Frames {
        rx,
        greeted: false,
        registration: Registration { core, conn_id },
    })
    .keep_alive(KeepAlive::default())
    .into_response()
}

/// The broadcaster's frames, as an SSE body.
struct Frames {
    rx: mpsc::UnboundedReceiver<String>,
    /// Whether the opening comment has been sent.
    greeted: bool,
    /// Held for the stream's life so the registration ends with it.
    registration: Registration,
}

/// Unregisters when the stream is dropped, which is the only signal an SSE
/// client has left: there is no close frame to wait for and no reply to miss.
struct Registration {
    core: Arc<Core>,
    conn_id: u64,
}

impl Drop for Registration {
    fn drop(&mut self) {
        self.core.v6_broadcaster.unregister(self.conn_id);
        tracing::debug!(conn_id = self.conn_id, "event stream closed");
    }
}

impl futures_core::Stream for Frames {
    type Item = Result<Event, Infallible>;

    /// The first poll answers with a comment nobody reads.
    ///
    /// The response head is not flushed until the body produces something, so
    /// without it a client waits for the first event before it even knows the
    /// stream opened - and a player that changes nothing for an hour would look
    /// like one that never connected.
    fn poll_next(mut self: Pin<&mut Self>, cx: &mut Context<'_>) -> Poll<Option<Self::Item>> {
        let _ = &self.registration;
        if !self.greeted {
            self.greeted = true;
            return Poll::Ready(Some(Ok(Event::default().comment("open"))));
        }
        self.rx
            .poll_recv(cx)
            .map(|frame| frame.map(|line| Ok(Event::default().data(line))))
    }
}
