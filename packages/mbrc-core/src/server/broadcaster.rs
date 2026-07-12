//! The broadcast registry: connected clients that opted into broadcasts (i.e.
//! did not set `no_broadcast`) register an outbound channel here. When a
//! MusicBee notification fires, the built frames are pushed to every registered
//! client.

use std::collections::HashMap;
use std::sync::Mutex;

use tokio::sync::mpsc::UnboundedSender;

/// Fan-out registry of per-connection outbound senders, keyed by connection id.
#[derive(Default)]
pub struct Broadcaster {
    clients: Mutex<HashMap<u64, UnboundedSender<String>>>,
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
        clients.retain(|_, sender| {
            frames
                .iter()
                .all(|frame| sender.send(frame.clone()).is_ok())
        });
    }

    /// Number of connected broadcast clients (for tests/diagnostics).
    pub fn client_count(&self) -> usize {
        self.lock().len()
    }
}

#[cfg(test)]
mod tests {
    use super::*;
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
}
