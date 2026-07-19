//! Per-connection IO. The first frame's shape routes the connection to either the
//! legacy V4/V5 [`Session`] or the V6 [`V6Session`] state machine (both pure, no
//! IO); inbound frames then run through the chosen machine. Outbound frames - both
//! request replies and broadcasts - funnel through one channel to a dedicated
//! writer task, so the read loop and the broadcast fan-out never race on the
//! socket. The writer's line terminator (`\r\n` legacy / `\n` V6) is fixed once the
//! connection is routed.

use std::net::SocketAddr;
use std::sync::{Arc, OnceLock};
use std::time::Duration;

use socket2::{SockRef, TcpKeepalive};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::tcp::OwnedWriteHalf;
use tokio::net::TcpStream;
use tokio::sync::mpsc::{self, UnboundedReceiver};
use tokio::sync::Notify;

use mbrc_wire::FrameAccumulator;

use crate::server::registry::Admit;
use crate::server::route::{self, Route};
use crate::server::session::{Outcome, Session};
use crate::server::session_v6::V6Session;
use crate::server::RegMeta;
use crate::state::Core;

/// Pre-serialized server keepalive frame (raw JSON; the writer adds framing).
/// Sent every `ping_interval_secs` (15s, the C# plugin's cadence) to broadcast
/// subscribers only. A live client answers each ping with `pong`; the ping also
/// fails fast on a half-open socket (the send errors, closing the connection).
/// Legacy-shaped: V6 has no event/keepalive surface in step 1, and a V6 socket
/// never subscribes to broadcasts, so it never receives this.
const PING_FRAME: &str = r#"{"context":"ping","data":""}"#;

/// Default line terminator until the connection is routed (legacy CRLF). Set once
/// on the first frame; V6 switches it to `\n`. A reply is never produced before
/// routing, so the default is only a safety net.
const DEFAULT_TERMINATOR: &str = "\r\n";

/// The negotiated protocol for a connection, chosen from its first frame's shape.
enum Proto {
    /// Not yet routed (no complete first frame seen).
    Pending,
    /// Legacy V4/V5 (`{"context":...}`).
    Legacy(Session),
    /// V6 clean-slate (`{"kind":...}`).
    V6(V6Session),
}

impl Proto {
    /// Registration metadata once the connection's handshake completes.
    fn reg_meta(&self) -> Option<RegMeta> {
        match self {
            Proto::Pending => None,
            Proto::Legacy(s) => s.reg_meta(),
            Proto::V6(s) => s.reg_meta(),
        }
    }

    /// Whether the handshake has completed (cheap; no allocation).
    fn handshaked(&self) -> bool {
        match self {
            Proto::Pending => false,
            Proto::Legacy(s) => s.protocol_version.is_some(),
            Proto::V6(s) => s.is_handshaked(),
        }
    }

    /// Whether this connection is a legacy broadcast subscriber (the only kind that
    /// receives fan-out + server pings in step 1).
    fn is_broadcast_main(&self) -> bool {
        matches!(self, Proto::Legacy(s) if !s.no_broadcast)
    }

    /// Feed one wire line to the routed state machine.
    fn handle(&mut self, line: &str, core: &Core) -> Outcome {
        match self {
            Proto::Legacy(s) => s.handle_frame(
                line,
                core.providers.as_ref(),
                Some(&core.now_playing),
                Some(core.cover_store.as_ref()),
                Some(core.metadata_cache.as_ref()),
            ),
            Proto::V6(s) => s.handle_frame(
                line,
                core.providers.as_ref(),
                Some(&core.now_playing),
                Some(core.cover_store.as_ref()),
                Some(core.metadata_cache.as_ref()),
            ),
            // Never reached: callers route Pending -> Legacy/V6 before handling.
            Proto::Pending => Outcome::nothing(),
        }
    }
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

    // The line terminator is fixed at routing (first frame). The writer reads it
    // per frame; it is always set before the first reply is produced.
    let terminator: Arc<OnceLock<&'static str>> = Arc::new(OnceLock::new());
    let (out_tx, out_rx) = mpsc::unbounded_channel::<String>();
    let writer_task = tokio::spawn(writer_loop(writer, out_rx, terminator.clone()));

    let conn_id = core.next_conn_id();
    // Tag every frame/decision emitted while handling this socket with its
    // conn_id, so the interleaved wire log (many overlapping iOS sockets) can be
    // attributed to a single connection. peer stays on the open/close lines.
    let conn_span = tracing::debug_span!("conn", conn_id);
    let mut proto = Proto::Pending;
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
                if !proto.handshaked() {
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
                // sockets and V6 connections are never pushed to - matching the
                // shipped C# plugin. A failed send means a dead socket -> close.
                let subscribes = registered && proto.is_broadcast_main();
                if subscribes && out_tx.send(PING_FRAME.to_string()).is_err() {
                    break;
                }
                continue;
            }
        };
        last_inbound = tokio::time::Instant::now();
        accumulator.push_bytes(&buf[..n]);

        while let Some(line) = accumulator.next_frame() {
            // handle is synchronous (no await), so holding the span guard across it
            // is safe. It covers the c2s/s2c wire logs and the handshake/drop
            // decisions emitted inside.
            let _guard = conn_span.enter();

            // Route on the first complete frame, before any reply is produced, so
            // the writer's terminator is set correctly.
            if matches!(proto, Proto::Pending) {
                match route::detect(&line) {
                    Route::Legacy => {
                        proto = Proto::Legacy(Session::default());
                        let _ = terminator.set("\r\n");
                    }
                    Route::V6 => {
                        proto = Proto::V6(V6Session::default());
                        let _ = terminator.set("\n");
                    }
                    Route::Unknown => {
                        tracing::debug!(
                            %peer,
                            conn_id,
                            "unroutable first frame; closing: {}",
                            crate::logging::redact_frame(&line, None)
                        );
                        closing = true;
                        break;
                    }
                }
            }

            let outcome = proto.handle(&line, &core);
            for reply in outcome.replies {
                if out_tx.send(reply).is_err() {
                    closing = true;
                    break;
                }
            }
            // Once the handshake completes, register the connection (enforcing the
            // per-client cap + superseding a stale main), then subscribe to
            // broadcasts if it is a legacy main. V6 connections register for caps
            // but never subscribe (no V6 event surface yet, and a V6 socket must
            // not receive V4-shaped broadcasts).
            if !registered {
                if let Some(meta) = proto.reg_meta() {
                    registered = true;
                    match core.registry.register(
                        conn_id,
                        meta.client_id.as_deref(),
                        meta.is_main,
                        peer.ip().is_loopback(),
                        shutdown.clone(),
                    ) {
                        Admit::Admitted => {
                            tracing::debug!(
                                platform = meta.platform.as_deref().unwrap_or("unknown"),
                                protocol = meta.protocol,
                                broadcasts = meta.is_main,
                                client_id = meta.client_id.as_deref().unwrap_or("none"),
                                "handshake complete; connection registered"
                            );
                            // A main subscribes to its protocol's broadcaster: V6
                            // events go to the V6 fan-out, V4/V5 frames to the legacy
                            // one - the two client sets never mix formats.
                            if meta.is_main {
                                if meta.protocol >= 6 {
                                    core.v6_broadcaster.register(conn_id, out_tx.clone());
                                } else {
                                    core.broadcaster.register(conn_id, out_tx.clone());
                                }
                            }
                        }
                        Admit::RejectedCap => {
                            tracing::debug!(
                                %peer,
                                conn_id,
                                client_id = meta.client_id.as_deref().unwrap_or("none"),
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
            }
            if outcome.close {
                closing = true;
                break;
            }
        }
    }

    // Unregister from both broadcasters (a conn is in at most one; the other is a
    // cheap no-op) so a V6 or legacy subscriber is cleaned up on close.
    core.broadcaster.unregister(conn_id);
    core.v6_broadcaster.unregister(conn_id);
    core.registry.unregister(
        conn_id,
        proto.reg_meta().and_then(|m| m.client_id).as_deref(),
    );
    drop(out_tx); // drop the last sender so the writer task drains and exits
    let _ = writer_task.await;
    // One-line post-mortem per socket: whether it ever handshaked, how many frames
    // it sent, and (legacy) how many commands were dropped for want of a handshake.
    // A closed iOS control socket shows handshaken=false with a high dropped count -
    // the whole bug in a single line, no grep archaeology.
    let (platform, handshaken, frames_in, dropped) = match &proto {
        Proto::Pending => (None, false, 0, 0),
        Proto::Legacy(s) => (
            s.platform.clone(),
            s.protocol_version.is_some(),
            s.frames_in,
            s.dropped_pre_handshake,
        ),
        Proto::V6(s) => (
            s.reg_meta().and_then(|m| m.platform),
            s.is_handshaked(),
            s.frames_in(),
            0,
        ),
    };
    tracing::debug!(
        %peer,
        conn_id,
        platform = platform.as_deref().unwrap_or("none"),
        handshaken,
        registered,
        frames_in,
        dropped_pre_handshake = dropped,
        duration_ms = opened_at.elapsed().as_millis() as u64,
        "connection closed"
    );
    Ok(())
}

/// Drain outbound frames to the socket until every sender is dropped. Each frame is
/// terminated with the connection's routed line terminator (`\r\n` legacy / `\n`
/// V6), which is set before the first frame is ever produced.
async fn writer_loop(
    mut writer: OwnedWriteHalf,
    mut out_rx: UnboundedReceiver<String>,
    terminator: Arc<OnceLock<&'static str>>,
) {
    while let Some(frame) = out_rx.recv().await {
        let term = terminator.get().copied().unwrap_or(DEFAULT_TERMINATOR);
        if writer
            .write_all(format!("{frame}{term}").as_bytes())
            .await
            .is_err()
        {
            break;
        }
    }
    writer.shutdown().await.ok();
}
