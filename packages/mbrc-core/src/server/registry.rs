//! The connection registry: bounds concurrent connections (per source IP and
//! per client-provided `client_id`) and supersedes a stale main socket when a
//! client reconnects.
//!
//! This is the leak/abuse backstop that lets us drop the aggressive idle-reap:
//! normal recycling is handled by the per-connection idle-timeout + OS TCP
//! keepalive, and this registry only catches a runaway before it accumulates.
//!
//! Catching a runaway means evicting, not refusing. Shipped iOS clients open a
//! socket per user action and never close it; turning the newest one away just
//! discards the user's request, so at the cap the stalest non-subscriber from
//! that IP is closed to make room instead.
//!
//! Two identities per connection: the server-assigned `conn_id` and the optional
//! client-provided `client_id` (Android v4 sends a UUID; iOS and old Android send
//! none). When a `client_id` is present we can group its sockets - enforce a
//! per-client cap and retire a superseded main. When it is absent we make no
//! grouping assumptions and rely on the per-IP cap + keepalive.

use std::collections::{HashMap, HashSet};
use std::net::IpAddr;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Instant;

use tokio::sync::Notify;

/// How long a connection must have been silent before the per-IP cap may evict
/// it. Well above a request's round trip, so a socket that is mid-exchange is
/// never taken; the sockets this actually frees have been silent for minutes.
const MIN_EVICT_IDLE_MS: u64 = 10_000;

/// Result of registering a handshaked connection.
#[derive(Debug, PartialEq, Eq)]
pub enum Admit {
    /// Admitted; proceed.
    Admitted,
    /// Rejected: the `client_id` is already at its concurrent-connection cap.
    RejectedCap,
}

/// Result of reserving a per-IP slot at accept time.
#[derive(Debug, PartialEq, Eq)]
pub enum IpAdmit {
    /// A slot was free.
    Admitted,
    /// The IP was at its cap, so this stale connection was closed to make room.
    /// Its slot is released asynchronously when its task finishes.
    Evicted(u64),
    /// At the cap with nothing worth evicting; the caller rejects the peer.
    Rejected,
}

/// What the registry can say about itself for a diagnostics report.
pub struct RegistryStats {
    /// Handshaked connections currently held.
    pub total: usize,
    /// How many of those are broadcast subscribers.
    pub subscribers: usize,
    /// Reserved per-IP slots (includes sockets that never handshaked), busiest
    /// first.
    pub by_ip: Vec<(String, usize)>,
    /// How long the most neglected connection has been silent.
    pub oldest_idle_secs: u64,
    pub evicted_total: u64,
    pub rejected_per_ip_total: u64,
}

/// The live sockets of one `client_id`.
#[derive(Default)]
struct ClientEntry {
    conns: HashSet<u64>,
    /// The current broadcast (main) connection, if any.
    main: Option<u64>,
}

/// What the registry keeps about one handshaked connection, so the per-IP cap
/// can pick a victim rather than turning the newcomer away.
struct ConnMeta {
    ip: IpAddr,
    /// Broadcast subscribers are never evicted: losing one silently stops every
    /// push to that client, which is far worse than refusing a socket.
    is_main: bool,
    /// Millis since [`ConnectionRegistry::origin`] of the last inbound frame.
    last_active_ms: u64,
    shutdown: Arc<Notify>,
}

#[derive(Default)]
struct Inner {
    by_ip: HashMap<IpAddr, usize>,
    by_client: HashMap<String, ClientEntry>,
    /// Per-connection close signal, fired to supersede a stale main.
    shutdown: HashMap<u64, Arc<Notify>>,
    /// Handshaked connections, for eviction. An un-handshaked socket holds an IP
    /// slot but is absent here; those are already reaped at
    /// `unhandshaked_timeout_secs`, so they need no eviction path.
    conns: HashMap<u64, ConnMeta>,
}

pub struct ConnectionRegistry {
    max_conns_per_client: usize,
    max_conns_per_ip: usize,
    inner: Mutex<Inner>,
    /// Monotonic zero for `last_active_ms`. A monotonic clock, not the wall
    /// clock, so a system time change can't make a connection look ancient.
    origin: Instant,
    evicted_total: AtomicU64,
    rejected_per_ip_total: AtomicU64,
    /// Test-only clock offset, so idle-ageing tests advance time instead of
    /// sleeping for the eviction threshold.
    #[cfg(test)]
    test_offset_ms: AtomicU64,
}

impl ConnectionRegistry {
    pub fn new(max_conns_per_client: usize, max_conns_per_ip: usize) -> Self {
        Self {
            max_conns_per_client,
            max_conns_per_ip,
            inner: Mutex::new(Inner::default()),
            origin: Instant::now(),
            evicted_total: AtomicU64::new(0),
            rejected_per_ip_total: AtomicU64::new(0),
            #[cfg(test)]
            test_offset_ms: AtomicU64::new(0),
        }
    }

    /// Millis since the registry's origin.
    fn now_ms(&self) -> u64 {
        let now = self.origin.elapsed().as_millis() as u64;
        #[cfg(test)]
        let now = now + self.test_offset_ms.load(Ordering::Relaxed);
        now
    }

    /// Move the clock forward, ageing every recorded connection at once.
    #[cfg(test)]
    fn advance(&self, ms: u64) {
        self.test_offset_ms.fetch_add(ms, Ordering::Relaxed);
    }

    fn lock(&self) -> std::sync::MutexGuard<'_, Inner> {
        self.inner.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// Reserve an IP slot at accept time. Loopback is always admitted (local
    /// tooling / the same-host debugger are never capped).
    ///
    /// At the cap this evicts rather than refusing, when it can. Refusing the
    /// newest socket is the wrong answer to a client that leaks: the newcomer is
    /// the one carrying the user's request, while the sockets already held are
    /// the abandoned ones. Shipped iOS clients open a socket per user action and
    /// never close it, so without this they walk into the cap and every later
    /// action is silently discarded (the app spins forever). The C# plugin had no
    /// cap at all and absorbed the leak; evicting keeps the bound without
    /// reintroducing that lockout.
    ///
    /// The victim is the least recently active non-subscriber from the same IP
    /// that has been silent for at least [`MIN_EVICT_IDLE_MS`]. Its slot is
    /// released when its task actually finishes, so the count sits one over the
    /// cap until then; the overshoot is bounded by evictions in flight.
    pub fn admit_ip(&self, ip: IpAddr) -> IpAdmit {
        if ip.is_loopback() {
            return IpAdmit::Admitted;
        }
        let now = self.now_ms();
        let mut inner = self.lock();
        let count = inner.by_ip.entry(ip).or_insert(0);
        if *count < self.max_conns_per_ip {
            *count += 1;
            return IpAdmit::Admitted;
        }
        let Some(victim) = Self::evictable(&inner, ip, now) else {
            self.rejected_per_ip_total.fetch_add(1, Ordering::Relaxed);
            return IpAdmit::Rejected;
        };
        if let Some(meta) = inner.conns.get(&victim) {
            meta.shutdown.notify_one();
        }
        *inner.by_ip.entry(ip).or_insert(0) += 1;
        self.evicted_total.fetch_add(1, Ordering::Relaxed);
        IpAdmit::Evicted(victim)
    }

    /// The connection this IP can most afford to lose, if any.
    fn evictable(inner: &Inner, ip: IpAddr, now: u64) -> Option<u64> {
        inner
            .conns
            .iter()
            .filter(|(_, meta)| {
                meta.ip == ip
                    && !meta.is_main
                    && now.saturating_sub(meta.last_active_ms) >= MIN_EVICT_IDLE_MS
            })
            .min_by_key(|(conn_id, meta)| (meta.last_active_ms, **conn_id))
            .map(|(conn_id, _)| *conn_id)
    }

    /// Record inbound activity, so eviction prefers genuinely abandoned sockets
    /// over ones the client is still using. Called per inbound frame; frames run
    /// at a few hundred per session, so taking the lock here is cheaper than
    /// plumbing a shared atomic through every connection.
    pub fn touch(&self, conn_id: u64) {
        let now = self.now_ms();
        let mut inner = self.lock();
        if let Some(meta) = inner.conns.get_mut(&conn_id) {
            meta.last_active_ms = now;
        }
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
        ip: IpAddr,
        client_id: Option<&str>,
        is_main: bool,
        loopback: bool,
        shutdown: Arc<Notify>,
    ) -> Admit {
        let now = self.now_ms();
        let mut inner = self.lock();

        let Some(client_id) = client_id else {
            inner.shutdown.insert(conn_id, shutdown.clone());
            // Ungrouped connections are exactly the ones that need the eviction
            // path: with no client_id there is no per-client cap holding them
            // back, so the per-IP cap is their only bound.
            inner.conns.insert(
                conn_id,
                ConnMeta {
                    ip,
                    is_main,
                    last_active_ms: now,
                    shutdown,
                },
            );
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

        inner.shutdown.insert(conn_id, shutdown.clone());
        inner.conns.insert(
            conn_id,
            ConnMeta {
                ip,
                is_main,
                last_active_ms: now,
                shutdown,
            },
        );

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
        // Drop the eviction entry too, so a closed connection is never chosen as
        // a victim (its `Notify` would fire into nothing and free no slot).
        inner.conns.remove(&conn_id);
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

    /// A snapshot of what is connected, for `report.json`. Without this a bug
    /// report shows the refusals but not the accumulation that caused them,
    /// which is the half that actually names the problem.
    pub fn stats(&self) -> RegistryStats {
        let now = self.now_ms();
        let inner = self.lock();
        let mut by_ip: Vec<(String, usize)> = inner
            .by_ip
            .iter()
            .map(|(ip, count)| (ip.to_string(), *count))
            .collect();
        // Busiest first: the runaway client is the one worth seeing.
        by_ip.sort_by(|a, b| b.1.cmp(&a.1).then_with(|| a.0.cmp(&b.0)));
        RegistryStats {
            total: inner.conns.len(),
            subscribers: inner.conns.values().filter(|m| m.is_main).count(),
            by_ip,
            oldest_idle_secs: inner
                .conns
                .values()
                .map(|m| now.saturating_sub(m.last_active_ms) / 1000)
                .max()
                .unwrap_or(0),
            evicted_total: self.evicted_total.load(Ordering::Relaxed),
            rejected_per_ip_total: self.rejected_per_ip_total.load(Ordering::Relaxed),
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
    /// Idle long enough for the per-IP cap to consider evicting.
    const STALE: u64 = MIN_EVICT_IDLE_MS + 1_000;

    #[test]
    fn per_ip_cap_admits_up_to_limit_then_rejects() {
        let r = ConnectionRegistry::new(20, 3);
        let peer = ip("192.168.1.5");
        assert_eq!(r.admit_ip(peer), IpAdmit::Admitted);
        assert_eq!(r.admit_ip(peer), IpAdmit::Admitted);
        assert_eq!(r.admit_ip(peer), IpAdmit::Admitted);
        // Nothing handshaked, so there is nothing to evict: still a refusal.
        assert_eq!(r.admit_ip(peer), IpAdmit::Rejected, "4th over the cap of 3");
        assert_eq!(r.ip_count(peer), 3);
        // Releasing frees a slot.
        r.release_ip(peer);
        assert_eq!(r.admit_ip(peer), IpAdmit::Admitted);
    }

    #[test]
    fn loopback_is_never_ip_capped() {
        let r = ConnectionRegistry::new(20, 2);
        let lo = ip("127.0.0.1");
        for _ in 0..10 {
            assert_eq!(r.admit_ip(lo), IpAdmit::Admitted);
        }
        assert_eq!(r.ip_count(lo), 0, "loopback is not counted");
    }

    /// The fix for the iOS lockout: at the cap, the socket the client abandoned
    /// longest ago goes, and the newcomer carrying the user's request gets in.
    #[test]
    fn at_the_cap_the_stalest_connection_is_evicted_not_the_newcomer() {
        let r = ConnectionRegistry::new(20, 2);
        let peer = ip("192.168.1.5");
        assert_eq!(r.admit_ip(peer), IpAdmit::Admitted);
        assert_eq!(r.admit_ip(peer), IpAdmit::Admitted);
        // conn 1 goes quiet 5s before conn 2 does.
        r.register(1, peer, None, false, false, notify());
        r.advance(5_000);
        r.register(2, peer, None, false, false, notify());
        r.advance(STALE);

        assert_eq!(r.admit_ip(peer), IpAdmit::Evicted(1));
        assert_eq!(r.stats().evicted_total, 1);
        assert_eq!(r.stats().rejected_per_ip_total, 0);
    }

    /// Evicting the broadcast socket would silently kill every push to that
    /// client, which is worse than refusing a connection. It is never a victim,
    /// even when it is the least recently active thing on the IP.
    #[test]
    fn a_broadcast_subscriber_is_never_evicted() {
        let r = ConnectionRegistry::new(20, 2);
        let peer = ip("192.168.1.5");
        r.admit_ip(peer);
        r.admit_ip(peer);
        r.register(1, peer, None, true, false, notify()); // the subscriber
        r.advance(60_000); // by far the stalest
        r.register(2, peer, None, false, false, notify());
        r.advance(STALE);

        assert_eq!(r.admit_ip(peer), IpAdmit::Evicted(2));
    }

    /// A socket that just spoke may be mid-exchange, so it is not a candidate.
    /// With nothing else to take, the old refusal behaviour stands.
    #[test]
    fn a_recently_active_connection_is_not_evicted() {
        let r = ConnectionRegistry::new(20, 1);
        let peer = ip("192.168.1.5");
        r.admit_ip(peer);
        r.register(1, peer, None, false, false, notify());

        assert_eq!(r.admit_ip(peer), IpAdmit::Rejected);
        assert_eq!(r.stats().rejected_per_ip_total, 1);
        // Once it has gone quiet it becomes fair game.
        r.advance(STALE);
        assert_eq!(r.admit_ip(peer), IpAdmit::Evicted(1));
    }

    #[test]
    fn touch_moves_a_connection_out_of_the_firing_line() {
        let r = ConnectionRegistry::new(20, 2);
        let peer = ip("192.168.1.5");
        r.admit_ip(peer);
        r.admit_ip(peer);
        r.register(1, peer, None, false, false, notify());
        r.advance(5_000);
        r.register(2, peer, None, false, false, notify());
        r.advance(STALE);
        // conn 1 speaks up, so conn 2 is now the stalest.
        r.touch(1);

        assert_eq!(r.admit_ip(peer), IpAdmit::Evicted(2));
    }

    #[test]
    fn eviction_never_crosses_ip_boundaries() {
        let r = ConnectionRegistry::new(20, 1);
        let noisy = ip("192.168.1.5");
        let quiet = ip("192.168.1.6");
        r.admit_ip(noisy);
        r.admit_ip(quiet);
        r.register(1, quiet, None, false, false, notify());
        r.advance(60_000); // conn 1 is stalest overall, but on a different IP
        r.register(2, noisy, None, false, false, notify());
        r.advance(STALE);

        assert_eq!(r.admit_ip(noisy), IpAdmit::Evicted(2));
    }

    #[test]
    fn a_closed_connection_is_not_a_victim() {
        let r = ConnectionRegistry::new(20, 1);
        let peer = ip("192.168.1.5");
        r.admit_ip(peer);
        r.register(1, peer, None, false, false, notify());
        r.advance(STALE);
        r.unregister(1, None);

        assert_eq!(
            r.admit_ip(peer),
            IpAdmit::Rejected,
            "conn 1 is gone; notifying it would free no slot"
        );
    }

    #[test]
    fn stats_report_what_is_connected() {
        let r = ConnectionRegistry::new(20, 40);
        let a = ip("192.168.1.5");
        let b = ip("192.168.1.6");
        r.admit_ip(a);
        r.admit_ip(a);
        r.admit_ip(b);
        r.register(1, a, None, true, false, notify());
        r.register(2, a, None, false, false, notify());
        r.register(3, b, None, false, false, notify());
        // Only conn 2 stays quiet across the next 30 seconds.
        r.advance(30_000);
        r.touch(1);
        r.touch(3);

        let stats = r.stats();
        assert_eq!(stats.total, 3);
        assert_eq!(stats.subscribers, 1);
        assert_eq!(
            stats.by_ip[0],
            ("192.168.1.5".to_string(), 2),
            "busiest first"
        );
        assert_eq!(stats.oldest_idle_secs, 30);
    }

    #[test]
    fn per_client_cap_rejects_newest() {
        let r = ConnectionRegistry::new(2, 40);
        assert_eq!(
            r.register(1, ip("10.0.0.1"), Some("cX"), false, false, notify()),
            Admit::Admitted
        );
        assert_eq!(
            r.register(2, ip("10.0.0.1"), Some("cX"), false, false, notify()),
            Admit::Admitted
        );
        assert_eq!(
            r.register(3, ip("10.0.0.1"), Some("cX"), false, false, notify()),
            Admit::RejectedCap,
            "3rd over the per-client cap of 2"
        );
        assert_eq!(r.client_count("cX"), 2);
        // A rejected conn was not recorded; freeing one admits again.
        r.unregister(1, Some("cX"));
        assert_eq!(
            r.register(4, ip("10.0.0.1"), Some("cX"), false, false, notify()),
            Admit::Admitted
        );
    }

    #[test]
    fn ungrouped_connections_have_no_client_cap() {
        let r = ConnectionRegistry::new(2, 40);
        for id in 0..10 {
            assert_eq!(
                r.register(id, ip("10.0.0.1"), None, false, false, notify()),
                Admit::Admitted
            );
        }
    }

    #[tokio::test]
    async fn new_main_supersedes_old_main_of_same_client() {
        let r = ConnectionRegistry::new(20, 40);
        let old = notify();
        assert_eq!(
            r.register(1, ip("10.0.0.1"), Some("cX"), true, false, old.clone()),
            Admit::Admitted
        );
        // A second main for the same client fires the old main's shutdown.
        assert_eq!(
            r.register(2, ip("10.0.0.1"), Some("cX"), true, false, notify()),
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
        r.register(1, ip("10.0.0.1"), Some("cX"), true, false, notify());
        assert_eq!(r.client_count("cX"), 1);
        r.unregister(1, Some("cX"));
        assert_eq!(r.client_count("cX"), 0);
    }
}
