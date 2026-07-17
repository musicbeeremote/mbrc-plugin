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
    // Tag every frame/decision emitted while handling this socket with its
    // conn_id, so the interleaved wire log (many overlapping iOS sockets) can be
    // attributed to a single connection. peer stays on the open/close lines.
    let conn_span = tracing::debug_span!("conn", conn_id);
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
                // Only sockets that connected but never completed the handshake are
                // idle-reaped (they negotiated nothing). Handshaked sockets - both
                // broadcast subscribers AND auxiliary request/response channels -
                // are NEVER idle-reaped, matching the shipped C# plugin: a real
                // client (iOS especially) keeps its command/event sockets open for
                // reuse and closes them itself, so reaping them mid-idle is exactly
                // what breaks its sync / leaves the app non-responsive. Dead sockets
                // are caught by OS TCP keepalive or the ping send failing; leaks are
                // bounded by the per-client / per-IP caps.
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
                // Ping ONLY broadcast subscribers; auxiliary request/response
                // sockets are never pushed to - matching the shipped C# plugin. A
                // failed send means a dead socket -> close.
                let subscribes = registered && !session.no_broadcast;
                if subscribes && out_tx.send(PING_FRAME.to_string()).is_err() {
                    break;
                }
                continue;
            }
        };
        last_inbound = tokio::time::Instant::now();
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
                    Admit::RejectedCap => {
                        tracing::debug!(
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
