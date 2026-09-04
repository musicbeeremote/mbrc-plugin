//! Per-connection IO.
//!
//! Inbound frames run through the pure [`Session`] state machine; outbound
//! frames - both request replies and broadcasts - funnel through one channel to
//! a dedicated writer task, so the read loop and the broadcast fan-out never
//! race on the socket.

use std::net::SocketAddr;
use std::sync::Arc;
use std::time::Duration;

use socket2::{SockRef, TcpKeepalive};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpStream;
use tokio::net::tcp::OwnedWriteHalf;
use tokio::sync::Notify;
use tokio::sync::mpsc::{self, UnboundedReceiver, UnboundedSender};

use mbrc_wire::{FrameAccumulator, frame_line};

use crate::server::registry::{Admit, Role};
use crate::server::session::Session;
use crate::state::Core;

/// Pre-serialized server keepalive frame (raw JSON; the writer adds framing).
/// Sent every `ping_interval_secs` to broadcast subscribers only. Its real job
/// is failing fast on a half-open socket: the send errors and the connection
/// closes, whether or not the client bothers to `pong`.
const PING_FRAME: &str = r#"{"context":"ping","data":""}"#;

/// Tags every frame and decision from one socket with its `conn_id`, so the
/// interleaved wire log (many overlapping iOS sockets) can be read per
/// connection. `peer` stays on the open/close lines.
///
/// INFO, not DEBUG: a span caches whether it is enabled at construction and can
/// never be revived, so a DEBUG span leaves every connection predating a capture
/// unattributable. It is held across the synchronous `handle_frame`, covering
/// the wire logs and handshake decisions inside it without crossing an await.
fn conn_span(conn_id: u64) -> tracing::Span {
    tracing::info_span!("conn", conn_id)
}

/// Logs a keepalive ping on the same `mbrc::wire` target as every other frame,
/// so a capture can tell a quiet client from a dead one. Logged here rather than
/// in the broadcaster because a ping belongs to one connection - hence the
/// `conn_id`, which broadcast lines lack. DEBUG, not TRACE: a diagnostics
/// capture only raises the level to DEBUG.
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

/// Drives one client connection to completion (EOF, close request, or IO error).
///
/// # Idle policy
///
/// Three rules, by what the socket has become. An un-handshaked socket is reaped
/// quickly. A broadcast subscriber is never reaped: reaping one breaks library
/// sync, and the shipped C# plugin did not either. An auxiliary channel sits
/// between - kept for reuse, closed once abandoned past `aux_idle_timeout_secs`,
/// because shipped iOS clients leak one per user action and would otherwise walk
/// into the per-IP cap. That cap's eviction, not this, is the real bound.
pub async fn run(stream: TcpStream, peer: SocketAddr, core: Arc<Core>) -> std::io::Result<()> {
    configure_socket(&stream, peer, core.config.tcp_keepalive_secs);
    let (mut reader, writer) = stream.into_split();

    let (out_tx, out_rx) = mpsc::unbounded_channel::<String>();
    let writer_task = tokio::spawn(writer_loop(writer, out_rx));

    let conn_id = core.next_conn_id();
    let timeouts = IdleTimeouts::from(&core.config);
    let mut ping_tick = ping_ticker(Duration::from_secs(core.config.ping_interval_secs)).await;
    let conn = Conn {
        span: conn_span(conn_id),
        shutdown: Arc::new(Notify::new()),
        core,
        peer,
        conn_id,
        out_tx,
    };

    let mut session = Session::default();
    let mut accumulator = FrameAccumulator::default();
    let mut buf = [0u8; 4096];
    let mut registered = false;
    let mut closing = false;
    let opened_at = tokio::time::Instant::now();
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
            _ = conn.shutdown.notified() => {
                tracing::debug!(%peer, conn_id, "connection superseded; closing");
                break;
            }
            _ = ping_tick.tick() => {
                match conn.on_ping_tick(&session, registered, last_inbound.elapsed(), &timeouts) {
                    Tick::Waiting => continue,
                    Tick::Retire => break,
                }
            }
        };
        last_inbound = tokio::time::Instant::now();
        conn.core.registry.touch(conn_id);
        accumulator.push_bytes(&buf[..n]);
        closing = conn.drain_frames(&mut session, &mut accumulator, &mut registered);

        // Such a peer never goes idle, so this is the only bound on it (#138).
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

    conn.core.broadcaster.unregister(conn_id);
    conn.core
        .registry
        .unregister(conn_id, session.client_id.as_deref());
    conn.log_closed(&session, registered, opened_at);
    drop(conn); // drop the last sender so the writer task drains and exits
    let _ = writer_task.await;
    Ok(())
}

/// The two deadlines an idle socket is measured against.
struct IdleTimeouts {
    unhandshaked: Duration,
    auxiliary: Duration,
}

impl From<&crate::config::Config> for IdleTimeouts {
    fn from(config: &crate::config::Config) -> Self {
        Self {
            unhandshaked: Duration::from_secs(config.unhandshaked_timeout_secs),
            auxiliary: Duration::from_secs(config.aux_idle_timeout_secs),
        }
    }
}

/// What a keepalive tick decided about the connection.
enum Tick {
    /// Keep it, and go back to waiting for input.
    Waiting,
    /// The idle policy retired this socket.
    Retire,
}

/// Everything about one connection that outlives a single trip round the loop.
///
/// Holds the outbound sender, so dropping it is what lets the writer task drain
/// and exit.
struct Conn {
    core: Arc<Core>,
    peer: SocketAddr,
    conn_id: u64,
    span: tracing::Span,
    shutdown: Arc<Notify>,
    out_tx: UnboundedSender<String>,
}

impl Conn {
    /// Applies the idle policy, then pings if the peer is a broadcast subscriber.
    ///
    /// The three rules and the reasoning behind them are on [`run`].
    fn on_ping_tick(
        &self,
        session: &Session,
        registered: bool,
        idle: Duration,
        timeouts: &IdleTimeouts,
    ) -> Tick {
        let (peer, conn_id) = (self.peer, self.conn_id);
        let idle_ms = idle.as_millis() as u64;

        if session.protocol_version.is_none() && idle >= timeouts.unhandshaked {
            tracing::debug!(%peer, conn_id, idle_ms, "closing un-handshaked idle connection");
            return Tick::Retire;
        }

        let subscribes = registered && !session.no_broadcast;
        let abandoned = registered
            && !subscribes
            && timeouts.auxiliary > Duration::ZERO
            && idle >= timeouts.auxiliary;
        if abandoned {
            tracing::debug!(%peer, conn_id, idle_ms, "closing idle auxiliary connection");
            return Tick::Retire;
        }

        if subscribes {
            if self.out_tx.send(PING_FRAME.to_string()).is_err() {
                return Tick::Retire;
            }
            log_ping(conn_id);
        }
        Tick::Waiting
    }

    /// Runs every whole frame the accumulator is holding.
    ///
    /// Returns whether the connection is finished, which a client can ask for
    /// outright and a departed writer task forces.
    fn drain_frames(
        &self,
        session: &mut Session,
        accumulator: &mut FrameAccumulator,
        registered: &mut bool,
    ) -> bool {
        while let Some(line) = accumulator.next_frame() {
            let _guard = self.span.enter();
            let outcome = session.handle_frame(
                &line,
                self.core.providers.as_ref(),
                Some(&self.core.now_playing),
                Some(self.core.cover_store.as_ref()),
                Some(self.core.metadata_cache.as_ref()),
            );
            for reply in outcome.replies {
                if self.out_tx.send(reply).is_err() {
                    return true;
                }
            }
            if !*registered && session.protocol_version.is_some() {
                *registered = true;
                if !register_and_subscribe(
                    &self.core,
                    session,
                    self.conn_id,
                    self.peer,
                    &self.shutdown,
                    &self.out_tx,
                ) {
                    return true;
                }
            }
            if outcome.close {
                return true;
            }
        }
        false
    }

    /// One-line post-mortem per socket.
    ///
    /// A closed iOS control socket shows `handshaken=false` with a high dropped
    /// count, which is the whole bug on one line.
    fn log_closed(&self, session: &Session, registered: bool, opened_at: tokio::time::Instant) {
        let (peer, conn_id) = (self.peer, self.conn_id);
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
    }
}

/// Applies the socket options every connection wants.
///
/// OS-level TCP keepalive drops dead half-open sockets, which is what lets the
/// server stop idle-reaping live handshaked connections - that was killing
/// library syncs.
fn configure_socket(stream: &TcpStream, peer: SocketAddr, keepalive_secs: u64) {
    stream.set_nodelay(true).ok();
    let keepalive = TcpKeepalive::new().with_time(Duration::from_secs(keepalive_secs));
    if let Err(e) = SockRef::from(stream).set_tcp_keepalive(&keepalive) {
        tracing::debug!(%peer, error = %e, "failed to set TCP keepalive");
    }
}

/// The server-keepalive ticker, with its immediate first tick consumed.
///
/// `read` in the select is cancel-safe, so dropping it when this fires loses no
/// bytes.
async fn ping_ticker(interval: Duration) -> tokio::time::Interval {
    let mut tick = tokio::time::interval(interval);
    tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);
    tick.tick().await;
    tick
}

/// Registers a freshly handshaked connection and subscribes it to broadcasts
/// unless it opted out, returning false when the per-client cap refuses it.
///
/// The refusal logs at WARN for the same reason the per-IP one does: the client
/// is about to stop working, and at the default level INFO would say nothing.
fn register_and_subscribe(
    core: &Arc<Core>,
    session: &Session,
    conn_id: u64,
    peer: SocketAddr,
    shutdown: &Arc<Notify>,
    out_tx: &UnboundedSender<String>,
) -> bool {
    let role = if session.no_broadcast {
        Role::Auxiliary
    } else {
        Role::Subscriber
    };
    match core.registry.register(
        conn_id,
        peer.ip(),
        session.client_id.as_deref(),
        role,
        shutdown.clone(),
    ) {
        Admit::Admitted => {
            tracing::debug!(
                platform = session.platform.as_deref().unwrap_or("unknown"),
                protocol = session.protocol_version.unwrap_or(0),
                broadcasts = role.is_subscriber(),
                client_id = session.client_id.as_deref().unwrap_or("none"),
                "handshake complete; connection registered"
            );
            if role == Role::Subscriber {
                core.broadcaster.register(conn_id, out_tx.clone());
            }
            true
        }
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
            false
        }
    }
}

/// Drains outbound frames to the socket until every sender is dropped.
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

    /// Attribution has to survive a level change: a span born at INFO can never
    /// be revived, so connections predating a capture would lose their `conn_id`
    /// for good. A `debug_span!` fails this.
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
