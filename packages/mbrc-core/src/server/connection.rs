//! Per-connection IO. Inbound frames run through the pure [`Session`] state
//! machine; outbound frames - both request replies and broadcasts - funnel
//! through one channel to a dedicated writer task, so the read loop and the
//! broadcast fan-out never race on the socket.

use std::net::SocketAddr;
use std::sync::Arc;
use std::time::Duration;

use socket2::{SockRef, TcpKeepalive};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::tcp::OwnedWriteHalf;
use tokio::net::TcpStream;
use tokio::sync::mpsc::{self, UnboundedReceiver};
use tokio::sync::Notify;

use mbrc_wire::{frame_line, FrameAccumulator};

use crate::server::registry::Admit;
use crate::server::session::Session;
use crate::state::Core;

/// Pre-serialized server keepalive frame (raw JSON; the writer adds framing).
/// Sent every `ping_interval_secs` (15s, the C# plugin's cadence) to broadcast
/// subscribers only. A live client answers each ping with `pong`; the ping also
/// fails fast on a half-open socket (the send errors, closing the connection).
const PING_FRAME: &str = r#"{"context":"ping","data":""}"#;

/// Tag every frame/decision emitted while handling one socket with its
/// `conn_id`, so the interleaved wire log (many overlapping iOS sockets) can be
/// attributed to a single connection. `peer` stays on the open/close lines.
///
/// Deliberately INFO, not DEBUG. A span caches whether it is enabled **when it
/// is constructed** and never re-evaluates it, and the reload layer cannot
/// revive one that was born as `Span::none()`. A diagnostics capture raises the
/// level only after the fact, so a DEBUG span left every connection that already
/// existed permanently unattributable - including the long-lived broadcast
/// socket, the one a push-path investigation is actually about. Measured on
/// 2026-08-30: the subscriber contributed zero attributed lines across sixteen
/// minutes, while a connection opened after the raise carried its `conn_id`
/// throughout.
///
/// INFO is the floor of every filter the plugin installs (both `logging::init`
/// fallbacks and all three levels in `capture.rs` / `NativeBridge.SetLogLevel`),
/// so the span is always live. The span emits nothing itself; its level only
/// decides whether it exists, which costs one span per connection and nothing
/// per frame.
fn conn_span(conn_id: u64) -> tracing::Span {
    tracing::info_span!("conn", conn_id)
}

/// Log a keepalive ping on the same `mbrc::wire` target as every other frame.
/// Pings are pushed, not replies, so nothing else in the wire log would show
/// them; without this a capture can't tell a quiet client from a dead one.
/// Logged here rather than in the broadcaster because a ping belongs to one
/// connection - hence the `conn_id`, which broadcast lines deliberately lack.
/// DEBUG, not TRACE: a diagnostics capture only raises the level to DEBUG, and
/// at 4 pings/min per subscriber the volume is nothing next to a real capture.
fn log_ping(conn_id: u64) {
    tracing::debug!(
        target: "mbrc::wire",
        dir = "s2c",
        kind = "ping",
        conn_id,
        context = crate::logging::frame_context(PING_FRAME),
        bytes = PING_FRAME.len(),
        "{PING_FRAME}"
    );
}

/// Drive one client connection to completion (EOF, close request, or IO error).
pub async fn run(stream: TcpStream, peer: SocketAddr, core: Arc<Core>) -> std::io::Result<()> {
    stream.set_nodelay(true).ok();
    // OS-level TCP keepalive so the kernel detects and drops dead half-open
    // sockets. This is the leak defense that lets us stop idle-reaping live
    // handshaked connections at the app layer (which was killing syncs).
    let keepalive =
        TcpKeepalive::new().with_time(Duration::from_secs(core.config.tcp_keepalive_secs));
    if let Err(e) = SockRef::from(&stream).set_tcp_keepalive(&keepalive) {
        tracing::debug!(%peer, error = %e, "failed to set TCP keepalive");
    }
    let (mut reader, writer) = stream.into_split();

    let (out_tx, out_rx) = mpsc::unbounded_channel::<String>();
    let writer_task = tokio::spawn(writer_loop(writer, out_rx));

    let conn_id = core.next_conn_id();
    let conn_span = conn_span(conn_id);
    let mut session = Session::default();
    let mut accumulator = FrameAccumulator::default();
    let mut buf = [0u8; 4096];
    let mut registered = false;
    let mut closing = false;
    let opened_at = tokio::time::Instant::now();

    // Per-connection close signal, fired by the registry to supersede a stale
    // main socket when the same client_id reconnects.
    let shutdown = Arc::new(Notify::new());

    // Server keepalive + un-handshaked reap. `read` in the select is cancel-safe,
    // so dropping it when the ping tick fires loses no bytes.
    let ping_interval = Duration::from_secs(core.config.ping_interval_secs);
    let unhandshaked_timeout = Duration::from_secs(core.config.unhandshaked_timeout_secs);
    let aux_idle_timeout = Duration::from_secs(core.config.aux_idle_timeout_secs);
    let mut ping_tick = tokio::time::interval(ping_interval);
    ping_tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);
    ping_tick.tick().await; // consume the immediate first tick
    let mut last_inbound = tokio::time::Instant::now();

    tracing::debug!(%peer, conn_id, "connection opened");
    while !closing {
        let n = tokio::select! {
            read = reader.read(&mut buf) => match read {
                Ok(0) => break,
                Ok(n) => n,
                Err(e) => {
                    tracing::debug!(%peer, error = %e, "read error");
                    break;
                }
            },
            // Superseded by a newer main for the same client_id, or a per-client
            // cap eviction: close after the in-flight frame.
            _ = shutdown.notified() => {
                tracing::debug!(%peer, conn_id, "connection superseded; closing");
                break;
            }
            _ = ping_tick.tick() => {
                // A socket that connected but never completed the handshake is
                // reaped quickly (it negotiated nothing). A broadcast subscriber is
                // never idle-reaped at all, matching the shipped C# plugin: a real
                // client keeps its event socket open and closes it itself, so
                // reaping it mid-idle is exactly what breaks sync / leaves the app
                // non-responsive. Auxiliary channels sit between the two - kept
                // open for reuse, but closed once abandoned past
                // `aux_idle_timeout_secs`, which defaults high enough that reuse
                // still works. Dead sockets are also caught by OS TCP keepalive or
                // the ping send failing; leaks are bounded by the per-client /
                // per-IP caps.
                if session.protocol_version.is_none() {
                    let idle = last_inbound.elapsed();
                    if idle >= unhandshaked_timeout {
                        tracing::debug!(
                            %peer,
                            conn_id,
                            idle_ms = idle.as_millis() as u64,
                            "closing un-handshaked idle connection"
                        );
                        break;
                    }
                }
                // Auxiliary (no_broadcast) sockets get a long idle window of
                // their own. Shipped iOS clients open one of these per user
                // action and never close it, so without a drain they accumulate
                // until the per-IP cap starts refusing. Subscribers are exempt -
                // reaping those is what broke library syncs - and the window is
                // deliberately generous, because iOS does reuse some aux sockets.
                // The per-IP eviction path is what actually guarantees a client
                // can't lock itself out; this is slow hygiene for long sessions.
                let subscribes = registered && !session.no_broadcast;
                if registered && !subscribes && aux_idle_timeout > Duration::ZERO {
                    let idle = last_inbound.elapsed();
                    if idle >= aux_idle_timeout {
                        tracing::debug!(
                            %peer,
                            conn_id,
                            idle_ms = idle.as_millis() as u64,
                            "closing idle auxiliary connection"
                        );
                        break;
                    }
                }
                if subscribes {
                    if out_tx.send(PING_FRAME.to_string()).is_err() {
                        break;
                    }
                    log_ping(conn_id);
                }
                continue;
            }
        };
        last_inbound = tokio::time::Instant::now();
        // Keep the registry's idea of activity current, so the per-IP cap evicts
        // sockets the client has abandoned rather than ones it is still using.
        core.registry.touch(conn_id);
        accumulator.push_bytes(&buf[..n]);

        while let Some(line) = accumulator.next_frame() {
            // handle_frame is synchronous (no await), so holding the span guard
            // across it is safe. It covers the c2s/s2c wire logs and the
            // handshake/drop decisions emitted inside.
            let _guard = conn_span.enter();
            let outcome = session.handle_frame(
                &line,
                core.providers.as_ref(),
                Some(&core.now_playing),
                Some(core.cover_store.as_ref()),
                Some(core.metadata_cache.as_ref()),
            );
            for reply in outcome.replies {
                if out_tx.send(reply).is_err() {
                    closing = true;
                    break;
                }
            }
            // Once the handshake completes, register the connection (enforcing the
            // per-client cap + superseding a stale main), then subscribe to
            // broadcasts unless the client opted out with no_broadcast.
            if !registered && session.protocol_version.is_some() {
                registered = true;
                let is_main = !session.no_broadcast;
                match core.registry.register(
                    conn_id,
                    peer.ip(),
                    session.client_id.as_deref(),
                    is_main,
                    peer.ip().is_loopback(),
                    shutdown.clone(),
                ) {
                    Admit::Admitted => {
                        tracing::debug!(
                            platform = session.platform.as_deref().unwrap_or("unknown"),
                            protocol = session.protocol_version.unwrap_or(0),
                            broadcasts = is_main,
                            client_id = session.client_id.as_deref().unwrap_or("none"),
                            "handshake complete; connection registered"
                        );
                        if is_main {
                            core.broadcaster.register(conn_id, out_tx.clone());
                        }
                    }
                    // WARN for the same reason as the per-IP refusal: the client
                    // is about to stop working and INFO would say nothing.
                    Admit::RejectedCap => {
                        tracing::warn!(
                            %peer,
                            conn_id,
                            client_id = session.client_id.as_deref().unwrap_or("none"),
                            "rejecting connection: per-client cap reached"
                        );
                        core.blocked.record(
                            peer.ip(),
                            peer.port(),
                            crate::server::blocked::BlockReason::PerClientCap,
                        );
                        closing = true;
                        break;
                    }
                }
            }
            if outcome.close {
                closing = true;
                break;
            }
        }

        // A frame that blew past the accumulator's cap with no terminator is an
        // unbounded-buffer attack or a badly broken peer. The accumulator has
        // already dropped the buffered bytes; close the socket (issue #138). Such
        // a peer is never idle by the reaper's reckoning - it is sending
        // constantly - so this is the only bound on it.
        if accumulator.overflowed() {
            tracing::warn!(
                %peer,
                conn_id,
                "frame exceeded max size ({} bytes) with no terminator; closing connection",
                mbrc_wire::DEFAULT_MAX_FRAME_BYTES
            );
            break;
        }
    }

    core.broadcaster.unregister(conn_id);
    core.registry
        .unregister(conn_id, session.client_id.as_deref());
    drop(out_tx); // drop the last sender so the writer task drains and exits
    let _ = writer_task.await;
    // One-line post-mortem per socket: whether it ever handshaked, how many
    // frames it sent, and how many commands were dropped for want of a
    // handshake. A closed iOS control socket shows handshaken=false with a high
    // dropped count - the whole bug in a single line, no grep archaeology.
    tracing::debug!(
        %peer,
        conn_id,
        platform = session.platform.as_deref().unwrap_or("none"),
        handshaken = session.protocol_version.is_some(),
        registered,
        frames_in = session.frames_in,
        dropped_pre_handshake = session.dropped_pre_handshake,
        duration_ms = opened_at.elapsed().as_millis() as u64,
        "connection closed"
    );
    Ok(())
}

/// Drain outbound frames to the socket until every sender is dropped.
async fn writer_loop(mut writer: OwnedWriteHalf, mut out_rx: UnboundedReceiver<String>) {
    while let Some(frame) = out_rx.recv().await {
        if writer
            .write_all(frame_line(&frame).as_bytes())
            .await
            .is_err()
        {
            break;
        }
    }
    writer.shutdown().await.ok();
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::logging::test_support::capture_wire_lines;

    /// The regression guard for attribution across a level change. A span caches
    /// whether it is enabled at construction and can never be revived, so a span
    /// born while the filter sat at INFO - which is where it sits until a
    /// capture raises it - has to be live at INFO or every connection that
    /// predates the capture loses its `conn_id` for good. A `debug_span!` here
    /// fails this.
    #[test]
    fn the_conn_span_survives_being_created_before_a_capture() {
        let at_info = tracing_subscriber::fmt()
            .with_max_level(tracing::Level::INFO)
            .with_writer(std::io::sink)
            .finish();

        tracing::subscriber::with_default(at_info, || {
            assert!(
                !conn_span(1).is_disabled(),
                "the conn span must be live at INFO, or a capture cannot attribute \
                 frames on any connection that already existed when it started"
            );
        });
    }

    /// The ping is the one pushed frame that belongs to a single connection, so
    /// its line has to carry the conn_id that broadcast lines don't.
    #[test]
    fn ping_is_logged_with_conn_id() {
        let lines = capture_wire_lines(|| log_ping(7));

        assert_eq!(lines.len(), 1, "{lines:?}");
        assert_eq!(lines[0].kind, "ping");
        assert_eq!(lines[0].dir, "s2c");
        assert_eq!(lines[0].context, "ping");
        assert_eq!(lines[0].conn_id, Some(7));
    }
}
