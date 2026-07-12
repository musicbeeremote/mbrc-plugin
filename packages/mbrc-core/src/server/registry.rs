//! The connection registry: bounds concurrent connections (per source IP and
//! per client-provided `client_id`) and supersedes a stale main socket when a
//! client reconnects.
//!
//! This is the leak/abuse backstop that lets us drop the aggressive idle-reap:
//! normal recycling is handled by the per-connection idle-timeout + OS TCP
//! keepalive, and this registry only catches a runaway before it accumulates.
//!
//! Two identities per connection: the server-assigned `conn_id` and the optional
//! client-provided `client_id` (Android v4 sends a UUID; iOS and old Android send
//! none). When a `client_id` is present we can group its sockets - enforce a
//! per-client cap and retire a superseded main. When it is absent we make no
//! grouping assumptions and rely on the per-IP cap + keepalive.

use std::collections::{HashMap, HashSet};
use std::net::IpAddr;
use std::sync::{Arc, Mutex};

use tokio::sync::Notify;

/// Result of registering a handshaked connection.
#[derive(Debug, PartialEq, Eq)]
pub enum Admit {
    /// Admitted; proceed.
    Admitted,
    /// Rejected: the `client_id` is already at its concurrent-connection cap.
    RejectedCap,
}

/// The live sockets of one `client_id`.
#[derive(Default)]
struct ClientEntry {
    conns: HashSet<u64>,
    /// The current broadcast (main) connection, if any.
    main: Option<u64>,
}

#[derive(Default)]
struct Inner {
    by_ip: HashMap<IpAddr, usize>,
    by_client: HashMap<String, ClientEntry>,
    /// Per-connection close signal, fired to supersede a stale main.
    shutdown: HashMap<u64, Arc<Notify>>,
}

pub struct ConnectionRegistry {
    max_conns_per_client: usize,
    max_conns_per_ip: usize,
    inner: Mutex<Inner>,
}

impl ConnectionRegistry {
    pub fn new(max_conns_per_client: usize, max_conns_per_ip: usize) -> Self {
        Self {
            max_conns_per_client,
            max_conns_per_ip,
            inner: Mutex::new(Inner::default()),
        }
    }

    fn lock(&self) -> std::sync::MutexGuard<'_, Inner> {
        self.inner.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// Reserve an IP slot at accept time. Loopback is always admitted (local
    /// tooling / the same-host debugger are never capped). Returns `false` when
    /// the IP is at its cap, in which case the caller rejects the connection.
    pub fn try_admit_ip(&self, ip: IpAddr) -> bool {
        if ip.is_loopback() {
            return true;
        }
        let mut inner = self.lock();
        let count = inner.by_ip.entry(ip).or_insert(0);
        if *count >= self.max_conns_per_ip {
            return false;
        }
        *count += 1;
        true
    }

    /// Release the IP slot reserved by [`try_admit_ip`](Self::try_admit_ip) when
    /// the connection ends. Loopback was never counted, so it is a no-op.
    pub fn release_ip(&self, ip: IpAddr) {
        if ip.is_loopback() {
            return;
        }
        let mut inner = self.lock();
        if let Some(count) = inner.by_ip.get_mut(&ip) {
            *count = count.saturating_sub(1);
            if *count == 0 {
                inner.by_ip.remove(&ip);
            }
        }
    }

    /// Register a handshaked connection: record its shutdown handle, enforce the
    /// per-`client_id` cap (only when a `client_id` is present and not loopback),
    /// and - for a main (broadcast) connection - retire any prior main of the
    /// same `client_id` by firing its shutdown signal. An ungrouped connection
    /// (no `client_id`) is always admitted with no cap or supersession.
    pub fn register(
        &self,
        conn_id: u64,
        client_id: Option<&str>,
        is_main: bool,
        loopback: bool,
        shutdown: Arc<Notify>,
    ) -> Admit {
        let mut inner = self.lock();

        let Some(client_id) = client_id else {
            inner.shutdown.insert(conn_id, shutdown);
            return Admit::Admitted;
        };

        let superseded = {
            let entry = inner.by_client.entry(client_id.to_string()).or_default();
            if !loopback && entry.conns.len() >= self.max_conns_per_client {
                return Admit::RejectedCap; // nothing recorded; caller closes
            }
            entry.conns.insert(conn_id);
            if is_main {
                entry.main.replace(conn_id) // prior main, if any
            } else {
                None
            }
        };

        inner.shutdown.insert(conn_id, shutdown);

        // Wake the superseded main's task so it closes. `notify_one` stores a
        // permit if the task isn't awaiting yet, so there is no lost-wakeup race.
        if let Some(old) = superseded.filter(|&old| old != conn_id) {
            if let Some(notify) = inner.shutdown.get(&old) {
                notify.notify_one();
            }
        }
        Admit::Admitted
    }

    /// Remove a connection's bookkeeping on close (shutdown handle + client
    /// grouping). IP release is separate - see [`release_ip`](Self::release_ip) -
    /// because a connection may end before it ever handshakes.
    pub fn unregister(&self, conn_id: u64, client_id: Option<&str>) {
        let mut inner = self.lock();
        inner.shutdown.remove(&conn_id);
        let Some(client_id) = client_id else {
            return;
        };
        let now_empty = if let Some(entry) = inner.by_client.get_mut(client_id) {
            entry.conns.remove(&conn_id);
            if entry.main == Some(conn_id) {
                entry.main = None;
            }
            entry.conns.is_empty()
        } else {
            false
        };
        if now_empty {
            inner.by_client.remove(client_id);
        }
    }

    #[cfg(test)]
    fn ip_count(&self, ip: IpAddr) -> usize {
        self.lock().by_ip.get(&ip).copied().unwrap_or(0)
    }
    #[cfg(test)]
    fn client_count(&self, client_id: &str) -> usize {
        self.lock()
            .by_client
            .get(client_id)
            .map(|e| e.conns.len())
            .unwrap_or(0)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ip(s: &str) -> IpAddr {
        s.parse().unwrap()
    }
    fn notify() -> Arc<Notify> {
        Arc::new(Notify::new())
    }

    #[test]
    fn per_ip_cap_admits_up_to_limit_then_rejects() {
        let r = ConnectionRegistry::new(20, 3);
        let peer = ip("192.168.1.5");
        assert!(r.try_admit_ip(peer));
        assert!(r.try_admit_ip(peer));
        assert!(r.try_admit_ip(peer));
        assert!(!r.try_admit_ip(peer), "4th over the cap of 3");
        assert_eq!(r.ip_count(peer), 3);
        // Releasing frees a slot.
        r.release_ip(peer);
        assert!(r.try_admit_ip(peer));
    }

    #[test]
    fn loopback_is_never_ip_capped() {
        let r = ConnectionRegistry::new(20, 2);
        let lo = ip("127.0.0.1");
        for _ in 0..10 {
            assert!(r.try_admit_ip(lo));
        }
        assert_eq!(r.ip_count(lo), 0, "loopback is not counted");
    }

    #[test]
    fn per_client_cap_rejects_newest() {
        let r = ConnectionRegistry::new(2, 40);
        assert_eq!(
            r.register(1, Some("cX"), false, false, notify()),
            Admit::Admitted
        );
        assert_eq!(
            r.register(2, Some("cX"), false, false, notify()),
            Admit::Admitted
        );
        assert_eq!(
            r.register(3, Some("cX"), false, false, notify()),
            Admit::RejectedCap,
            "3rd over the per-client cap of 2"
        );
        assert_eq!(r.client_count("cX"), 2);
        // A rejected conn was not recorded; freeing one admits again.
        r.unregister(1, Some("cX"));
        assert_eq!(
            r.register(4, Some("cX"), false, false, notify()),
            Admit::Admitted
        );
    }

    #[test]
    fn ungrouped_connections_have_no_client_cap() {
        let r = ConnectionRegistry::new(2, 40);
        for id in 0..10 {
            assert_eq!(
                r.register(id, None, false, false, notify()),
                Admit::Admitted
            );
        }
    }

    #[tokio::test]
    async fn new_main_supersedes_old_main_of_same_client() {
        let r = ConnectionRegistry::new(20, 40);
        let old = notify();
        assert_eq!(
            r.register(1, Some("cX"), true, false, old.clone()),
            Admit::Admitted
        );
        // A second main for the same client fires the old main's shutdown.
        assert_eq!(
            r.register(2, Some("cX"), true, false, notify()),
            Admit::Admitted
        );
        // The old main's notify was signalled (permit stored) -> ready now.
        tokio::time::timeout(std::time::Duration::from_millis(200), old.notified())
            .await
            .expect("superseded main should have been notified");
    }

    #[test]
    fn unregister_cleans_empty_client_entries() {
        let r = ConnectionRegistry::new(20, 40);
        r.register(1, Some("cX"), true, false, notify());
        assert_eq!(r.client_count("cX"), 1);
        r.unregister(1, Some("cX"));
        assert_eq!(r.client_count("cX"), 0);
    }
}
