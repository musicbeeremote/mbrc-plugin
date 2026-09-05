//! The connection registry: bounds concurrent connections (per source IP and
//! per client-provided `client_id`) and supersedes a stale main socket when a
//! client reconnects.
//!
//! Normal recycling is the per-connection idle timeout plus OS TCP keepalive;
//! this only catches a runaway.
//!
//! At the cap it evicts rather than refuses: shipped iOS clients leak a socket
//! per user action, and turning the newest one away discards the user's
//! request, so the stalest non-subscriber from that IP goes instead.
//!
//! Sockets can only be grouped when the client sends a `client_id` (Android v4
//! does, iOS and old Android do not); without one, only the per-IP cap applies.

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

/// What a connection is for, which decides whether it may be evicted.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Role {
    /// Receives broadcasts. Never evicted: losing one silently stops every push
    /// to that client, which is worse than refusing a new socket.
    Subscriber,
    /// Request and response only, opened with `no_broadcast`. Evictable.
    Auxiliary,
}

impl Role {
    pub fn is_subscriber(self) -> bool {
        self == Role::Subscriber
    }
}

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
///
/// The counts measure different populations on purpose, and the field names say
/// which: `handshaked` includes loopback and excludes un-negotiated sockets,
/// `slots_by_ip` is the opposite on both counts.
pub struct RegistryStats {
    /// Handshaked connections currently held, loopback included.
    pub handshaked: usize,
    /// How many of those are broadcast subscribers.
    pub subscribers: usize,
    /// Sockets holding a per-IP slot without having handshaked. They count
    /// against the cap, so a report about the cap has to show them.
    pub unhandshaked: usize,
    /// Reserved per-IP slots, busiest first. Loopback is exempt from the cap and
    /// so never appears here, and un-handshaked sockets do.
    pub slots_by_ip: Vec<(String, usize)>,
    /// How long the most neglected *non-subscriber* has been silent. Aux sockets
    /// are what the reaper and the per-IP eviction act on, so a large value
    /// means abandoned sockets are piling up.
    pub oldest_aux_idle_secs: u64,
    /// How long the quietest subscriber has been silent inbound.
    ///
    /// **Not a liveness signal.** iOS never answers a ping, so this climbs
    /// without bound on a healthy event socket; subscriber health is the ping
    /// *send* succeeding plus TCP keepalive. Kept apart from the aux figure so
    /// it stops making a working connection look stale.
    pub subscriber_idle_secs: u64,
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
    role: Role,
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

    /// Moves the clock forward, ageing every recorded connection at once.
    #[cfg(test)]
    fn advance(&self, ms: u64) {
        self.test_offset_ms.fetch_add(ms, Ordering::Relaxed);
    }

    fn lock(&self) -> std::sync::MutexGuard<'_, Inner> {
        self.inner.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// Reserves an IP slot at accept time. Loopback is never capped.
    ///
    /// At the cap this evicts rather than refuses: against a client that leaks
    /// sockets the newcomer carries the user's request and the held sockets are
    /// the abandoned ones, so refusing it made iOS lock itself out. The victim is
    /// the least recently active non-subscriber from that IP, silent for at least
    /// `MIN_EVICT_IDLE_MS`; its slot frees when its task finishes, so the count
    /// sits one over the cap until then.
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
                    && !meta.role.is_subscriber()
                    && now.saturating_sub(meta.last_active_ms) >= MIN_EVICT_IDLE_MS
            })
            .min_by_key(|(conn_id, meta)| (meta.last_active_ms, **conn_id))
            .map(|(conn_id, _)| *conn_id)
    }

    /// Records inbound activity, so eviction prefers genuinely abandoned sockets
    /// over ones the client is still using. Called per inbound frame - a few
    /// hundred a session, so the lock is cheaper than a plumbed-through atomic.
    pub fn touch(&self, conn_id: u64) {
        let now = self.now_ms();
        let mut inner = self.lock();
        if let Some(meta) = inner.conns.get_mut(&conn_id) {
            meta.last_active_ms = now;
        }
    }

    /// Releases the IP slot reserved by [`admit_ip`](Self::admit_ip) when
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

    /// Registers a handshaked connection: record its shutdown handle, enforce the
    /// per-`client_id` cap, and retire any prior main of the same `client_id`.
    /// Both need a `client_id`; without one the connection is always admitted.
    pub fn register(
        &self,
        conn_id: u64,
        ip: IpAddr,
        client_id: Option<&str>,
        role: Role,
        shutdown: Arc<Notify>,
    ) -> Admit {
        // Derived rather than passed: a caller handing in both an address and a
        // flag about it is a caller that can contradict itself.
        let loopback = ip.is_loopback();
        let now = self.now_ms();
        let mut inner = self.lock();

        let Some(client_id) = client_id else {
            inner.shutdown.insert(conn_id, shutdown.clone());
            // With no client_id there is no per-client cap, so the per-IP cap is
            // an ungrouped connection's only bound.
            inner.conns.insert(
                conn_id,
                ConnMeta {
                    ip,
                    role,
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
            if role.is_subscriber() {
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
                role,
                last_active_ms: now,
                shutdown,
            },
        );

        // Wake the superseded main's task so it closes. `notify_one` stores a
        // permit if the task isn't awaiting yet, so there is no lost-wakeup race.
        if let Some(old) = superseded.filter(|&old| old != conn_id)
            && let Some(notify) = inner.shutdown.get(&old)
        {
            notify.notify_one();
        }
        Admit::Admitted
    }

    /// Removes a connection's bookkeeping on close (shutdown handle + client
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

    /// A snapshot of what is connected, for `report.json`. Without it a bug
    /// report shows the refusals but not the accumulation behind them.
    pub fn stats(&self) -> RegistryStats {
        let now = self.now_ms();
        let inner = self.lock();
        let mut slots_by_ip: Vec<(String, usize)> = inner
            .by_ip
            .iter()
            .map(|(ip, count)| (ip.to_string(), *count))
            .collect();
        // Busiest first: the runaway client is the one worth seeing.
        slots_by_ip.sort_by(|a, b| b.1.cmp(&a.1).then_with(|| a.0.cmp(&b.0)));

        let idle_secs = |m: &ConnMeta| now.saturating_sub(m.last_active_ms) / 1000;
        // Split the idle figures by role: a subscriber is silent by design
        // (iOS never pongs), and lumping it in made a healthy plugin look stalled.
        let (subs, aux): (Vec<&ConnMeta>, Vec<&ConnMeta>) =
            inner.conns.values().partition(|m| m.role.is_subscriber());
        // Slots are only reserved for non-loopback peers, so the handshaked
        // connections that can account for one are the non-loopback ones.
        let slots_reserved: usize = inner.by_ip.values().sum();
        let handshaked_remote = inner.conns.values().filter(|m| !m.ip.is_loopback()).count();

        RegistryStats {
            handshaked: inner.conns.len(),
            subscribers: subs.len(),
            unhandshaked: slots_reserved.saturating_sub(handshaked_remote),
            slots_by_ip,
            oldest_aux_idle_secs: aux.iter().copied().map(idle_secs).max().unwrap_or(0),
            subscriber_idle_secs: subs.iter().copied().map(idle_secs).max().unwrap_or(0),
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

    /// At the cap the longest-abandoned socket goes and the newcomer gets in -
    /// the fix for the iOS lockout.
    #[test]
    fn at_the_cap_the_stalest_connection_is_evicted_not_the_newcomer() {
        let r = ConnectionRegistry::new(20, 2);
        let peer = ip("192.168.1.5");
        assert_eq!(r.admit_ip(peer), IpAdmit::Admitted);
        assert_eq!(r.admit_ip(peer), IpAdmit::Admitted);
        // conn 1 goes quiet 5s before conn 2 does.
        r.register(1, peer, None, Role::Auxiliary, notify());
        r.advance(5_000);
        r.register(2, peer, None, Role::Auxiliary, notify());
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
        r.register(1, peer, None, Role::Subscriber, notify());
        r.advance(60_000); // by far the stalest
        r.register(2, peer, None, Role::Auxiliary, notify());
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
        r.register(1, peer, None, Role::Auxiliary, notify());

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
        r.register(1, peer, None, Role::Auxiliary, notify());
        r.advance(5_000);
        r.register(2, peer, None, Role::Auxiliary, notify());
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
        r.register(1, quiet, None, Role::Auxiliary, notify());
        r.advance(60_000); // conn 1 is stalest overall, but on a different IP
        r.register(2, noisy, None, Role::Auxiliary, notify());
        r.advance(STALE);

        assert_eq!(r.admit_ip(noisy), IpAdmit::Evicted(2));
    }

    #[test]
    fn a_closed_connection_is_not_a_victim() {
        let r = ConnectionRegistry::new(20, 1);
        let peer = ip("192.168.1.5");
        r.admit_ip(peer);
        r.register(1, peer, None, Role::Auxiliary, notify());
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
        r.register(1, a, None, Role::Subscriber, notify());
        r.register(2, a, None, Role::Auxiliary, notify());
        r.register(3, b, None, Role::Auxiliary, notify());
        // Only conn 2 stays quiet across the next 30 seconds.
        r.advance(30_000);
        r.touch(1);
        r.touch(3);

        let stats = r.stats();
        assert_eq!(stats.handshaked, 3);
        assert_eq!(stats.subscribers, 1);
        assert_eq!(stats.unhandshaked, 0);
        assert_eq!(
            stats.slots_by_ip[0],
            ("192.168.1.5".to_string(), 2),
            "busiest first"
        );
        assert_eq!(stats.oldest_aux_idle_secs, 30);
    }

    /// iOS never answers a ping, so a subscriber's inbound activity never
    /// advances; counting it as the oldest idle connection made a healthy plugin
    /// look stalled.
    #[test]
    fn a_silent_subscriber_does_not_masquerade_as_a_stale_connection() {
        let r = ConnectionRegistry::new(20, 40);
        let peer = ip("192.168.1.5");
        r.admit_ip(peer);
        r.admit_ip(peer);
        r.register(1, peer, None, Role::Subscriber, notify()); // subscriber, never speaks
        r.register(2, peer, None, Role::Auxiliary, notify());
        r.advance(600_000);
        r.touch(2); // the aux socket is active

        let stats = r.stats();
        assert_eq!(stats.oldest_aux_idle_secs, 0, "the aux socket just spoke");
        assert_eq!(
            stats.subscriber_idle_secs, 600,
            "the subscriber's silence is still reported, but on its own field"
        );
    }

    /// Loopback is handshaked but reserves no slot, so `unhandshaked` must not
    /// underflow between the two counts.
    #[test]
    fn loopback_counts_as_handshaked_but_reserves_no_slot() {
        let r = ConnectionRegistry::new(20, 40);
        let lo = ip("127.0.0.1");
        r.admit_ip(lo);
        r.admit_ip(lo);
        r.register(1, lo, None, Role::Auxiliary, notify());
        r.register(2, lo, None, Role::Auxiliary, notify());

        let stats = r.stats();
        assert_eq!(stats.handshaked, 2);
        assert!(stats.slots_by_ip.is_empty(), "loopback is never capped");
        assert_eq!(stats.unhandshaked, 0, "must not underflow");
    }

    /// A socket holding a slot but never negotiating still counts against the
    /// cap, so a report about the cap has to show it.
    #[test]
    fn stats_count_slots_held_by_sockets_that_never_handshaked() {
        let r = ConnectionRegistry::new(20, 40);
        let peer = ip("192.168.1.5");
        r.admit_ip(peer); // handshakes below
        r.admit_ip(peer); // never handshakes
        r.admit_ip(peer); // never handshakes
        r.register(1, peer, None, Role::Auxiliary, notify());

        let stats = r.stats();
        assert_eq!(stats.handshaked, 1);
        assert_eq!(stats.unhandshaked, 2);
        assert_eq!(stats.slots_by_ip[0].1, 3);
    }

    #[test]
    fn per_client_cap_rejects_newest() {
        let r = ConnectionRegistry::new(2, 40);
        assert_eq!(
            r.register(1, ip("10.0.0.1"), Some("cX"), Role::Auxiliary, notify()),
            Admit::Admitted
        );
        assert_eq!(
            r.register(2, ip("10.0.0.1"), Some("cX"), Role::Auxiliary, notify()),
            Admit::Admitted
        );
        assert_eq!(
            r.register(3, ip("10.0.0.1"), Some("cX"), Role::Auxiliary, notify()),
            Admit::RejectedCap,
            "3rd over the per-client cap of 2"
        );
        assert_eq!(r.client_count("cX"), 2);
        // A rejected conn was not recorded; freeing one admits again.
        r.unregister(1, Some("cX"));
        assert_eq!(
            r.register(4, ip("10.0.0.1"), Some("cX"), Role::Auxiliary, notify()),
            Admit::Admitted
        );
    }

    #[test]
    fn ungrouped_connections_have_no_client_cap() {
        let r = ConnectionRegistry::new(2, 40);
        for id in 0..10 {
            assert_eq!(
                r.register(id, ip("10.0.0.1"), None, Role::Auxiliary, notify()),
                Admit::Admitted
            );
        }
    }

    #[tokio::test]
    async fn new_main_supersedes_old_main_of_same_client() {
        let r = ConnectionRegistry::new(20, 40);
        let old = notify();
        assert_eq!(
            r.register(1, ip("10.0.0.1"), Some("cX"), Role::Subscriber, old.clone()),
            Admit::Admitted
        );
        // A second main for the same client fires the old main's shutdown.
        assert_eq!(
            r.register(2, ip("10.0.0.1"), Some("cX"), Role::Subscriber, notify()),
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
        r.register(1, ip("10.0.0.1"), Some("cX"), Role::Subscriber, notify());
        assert_eq!(r.client_count("cX"), 1);
        r.unregister(1, Some("cX"));
        assert_eq!(r.client_count("cX"), 0);
    }
}
