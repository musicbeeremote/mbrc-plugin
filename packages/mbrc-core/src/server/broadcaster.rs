//! The broadcast registry: connected clients that opted into broadcasts (i.e.
//! did not set `no_broadcast`) register an outbound channel here. When a
//! MusicBee notification fires, the built frames are pushed to every registered
//! client.

use std::collections::HashMap;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Mutex;

use tokio::sync::mpsc::UnboundedSender;

/// Fan-out registry of per-connection outbound senders, keyed by connection id.
#[derive(Default)]
pub struct Broadcaster {
    clients: Mutex<HashMap<u64, UnboundedSender<String>>>,
    /// Monotonic counter for the wire log. A broadcast has no connection, so it
    /// can't borrow `Session::frames_in`; without a sequence the pushed lines
    /// would be timestamp-only and hard to interleave against the inbound side.
    seq: AtomicU64,
}

impl Broadcaster {
    /// Lock the client map, recovering from a poisoned mutex. A panic elsewhere
    /// while the lock is held must not permanently disable every broadcast;
    /// mirrors `ConnectionRegistry::lock`.
    fn lock(&self) -> std::sync::MutexGuard<'_, HashMap<u64, UnboundedSender<String>>> {
        self.clients.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// Register a connection's outbound sender for broadcasts.
    pub fn register(&self, conn_id: u64, sender: UnboundedSender<String>) {
        self.lock().insert(conn_id, sender);
    }

    /// Remove a connection (on disconnect).
    pub fn unregister(&self, conn_id: u64) {
        self.lock().remove(&conn_id);
    }

    /// Push raw frames to every registered client. Closed channels are pruned.
    pub fn broadcast(&self, frames: &[String]) {
        if frames.is_empty() {
            return;
        }
        let mut clients = self.lock();
        // Read the fan-out width under the same lock as the send: a push to zero
        // subscribers has to be distinguishable in the log from no event at all.
        let subscribers = clients.len();
        clients.retain(|_, sender| {
            frames
                .iter()
                .all(|frame| sender.send(frame.clone()).is_ok())
        });
        let pruned = subscribers - clients.len();
        drop(clients);
        self.log_frames(frames, subscribers, pruned);
    }

    /// Emit one wire line per pushed frame. Broadcasts are logged here rather
    /// than per subscriber so the volume tracks events, not connections, and
    /// they sit outside the `conn` span (they belong to no connection) - which
    /// is what tells them apart from `session.rs`'s per-connection `s2c` lines.
    fn log_frames(&self, frames: &[String], subscribers: usize, pruned: usize) {
        // The whole path is DEBUG, not TRACE: a diagnostics capture only raises
        // the level to DEBUG, so a TRACE-only push path would never reach a bug
        // report - which is the point of logging it at all.
        if !tracing::enabled!(target: "mbrc::wire", tracing::Level::DEBUG) {
            return;
        }
        for frame in frames {
            let seq = self.seq.fetch_add(1, Ordering::Relaxed);
            let context = crate::logging::frame_context(frame);
            // DEBUG caps list bodies to a sample + schema summary, TRACE keeps
            // the whole body. Mandatory here: `nowplayingcover` and
            // `nowplayinglyrics` push base64 blobs.
            if tracing::enabled!(target: "mbrc::wire", tracing::Level::TRACE) {
                tracing::trace!(
                    target: "mbrc::wire",
                    dir = "s2c",
                    kind = "broadcast",
                    seq,
                    context,
                    subscribers,
                    bytes = frame.len(),
                    "{}",
                    crate::logging::redact_frame(frame, None)
                );
            } else {
                tracing::debug!(
                    target: "mbrc::wire",
                    dir = "s2c",
                    kind = "broadcast",
                    seq,
                    context,
                    subscribers,
                    bytes = frame.len(),
                    "{}",
                    crate::logging::redact_frame(frame, Some(crate::logging::WIRE_LIST_SAMPLE))
                );
            }
        }
        if pruned > 0 {
            tracing::debug!(
                target: "mbrc::wire",
                kind = "broadcast",
                pruned,
                subscribers,
                "pruned closed broadcast channels"
            );
        }
    }

    /// Number of connected broadcast clients (for tests/diagnostics).
    pub fn client_count(&self) -> usize {
        self.lock().len()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::logging::test_support::{capture_wire_lines, WireLine};
    use tokio::sync::mpsc;

    #[test]
    fn broadcast_reaches_registered_and_prunes_closed() {
        let b = Broadcaster::default();
        let (tx1, mut rx1) = mpsc::unbounded_channel();
        let (tx2, rx2) = mpsc::unbounded_channel();
        b.register(1, tx1);
        b.register(2, tx2);
        assert_eq!(b.client_count(), 2);

        // Drop client 2's receiver so its channel is closed.
        drop(rx2);
        b.broadcast(&["{\"context\":\"playermute\",\"data\":true}".to_string()]);

        assert_eq!(
            rx1.try_recv().unwrap(),
            "{\"context\":\"playermute\",\"data\":true}"
        );
        // Client 2 was pruned when its send failed.
        assert_eq!(b.client_count(), 1);
    }

    /// Every pushed frame gets its own wire line, carrying the fan-out width.
    #[test]
    fn broadcast_logs_one_line_per_frame_with_subscriber_count() {
        let lines = capture_wire_lines(|| {
            let b = Broadcaster::default();
            let (tx1, _rx1) = mpsc::unbounded_channel();
            let (tx2, _rx2) = mpsc::unbounded_channel();
            b.register(1, tx1);
            b.register(2, tx2);
            b.broadcast(&[
                "{\"context\":\"playermute\",\"data\":true}".to_string(),
                "{\"context\":\"playerstate\",\"data\":\"playing\"}".to_string(),
            ]);
        });

        let pushed: Vec<&WireLine> = lines.iter().filter(|l| l.kind == "broadcast").collect();
        assert_eq!(pushed.len(), 2, "{lines:?}");
        assert_eq!(pushed[0].context, "playermute");
        assert_eq!(pushed[1].context, "playerstate");
        for line in pushed {
            assert_eq!(line.dir, "s2c");
            assert_eq!(line.subscribers, Some(2));
            assert!(line.seq_present);
        }
    }

    /// A push that reached nobody is the diagnostic question the issue is about,
    /// so it must still produce a line - with `subscribers = 0`.
    #[test]
    fn broadcast_to_zero_subscribers_is_still_logged() {
        let lines = capture_wire_lines(|| {
            let b = Broadcaster::default();
            b.broadcast(&["{\"context\":\"playermute\",\"data\":true}".to_string()]);
        });

        let pushed: Vec<&WireLine> = lines.iter().filter(|l| l.kind == "broadcast").collect();
        assert_eq!(pushed.len(), 1, "{lines:?}");
        assert_eq!(pushed[0].context, "playermute");
        assert_eq!(pushed[0].subscribers, Some(0));
    }
}
