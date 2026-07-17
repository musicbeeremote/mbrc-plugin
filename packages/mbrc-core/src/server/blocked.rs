//! In-memory log of recently rejected connection attempts, surfaced to the
//! settings panel so a user can see why a device was blocked (issue #84).
//!
//! A bounded ring buffer of the last [`MAX_ENTRIES`] rejections, newest first.
//! Not persisted - it resets when the plugin reloads (the reject reasons are a
//! live-debugging aid, not an audit log). Populated at the accept-loop /
//! connection reject sites (`server::accept_loop`, `connection::run`) and read
//! back over FFI via `HostQueryType::RecentBlocked`.

use std::collections::VecDeque;
use std::net::IpAddr;
use std::sync::Mutex;
use std::time::{SystemTime, UNIX_EPOCH};

use crate::ffi::dtos::BlockedConnection;

/// How many recent rejections to keep. Older entries are dropped.
const MAX_ENTRIES: usize = 50;

/// Why a connection attempt was rejected. The string form is what the settings
/// panel shows; keep it user-facing (not jargon).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum BlockReason {
    /// Client-address filter (Range/Specific mode) did not admit the peer.
    AddressNotAllowed,
    /// Per-IP connection cap reached (too many sockets from one address).
    PerIpCap,
    /// Per-client cap reached (too many sockets from one client id).
    PerClientCap,
}

impl BlockReason {
    fn as_str(self) -> &'static str {
        match self {
            BlockReason::AddressNotAllowed => "Address not allowed",
            BlockReason::PerIpCap => "Too many connections from this address",
            BlockReason::PerClientCap => "Too many connections from this client",
        }
    }
}

/// A thread-safe ring buffer of recent [`BlockedConnection`] entries.
#[derive(Default)]
pub struct BlockedLog {
    entries: Mutex<VecDeque<BlockedConnection>>,
}

impl BlockedLog {
    /// Record a rejected attempt. Newest is kept at the front; the oldest is
    /// evicted once the buffer is full.
    pub fn record(&self, ip: IpAddr, port: u16, reason: BlockReason) {
        let entry = BlockedConnection {
            unix_ms: now_unix_ms(),
            ip: ip.to_string(),
            port,
            reason: reason.as_str().to_string(),
        };
        let mut q = self.lock();
        q.push_front(entry);
        while q.len() > MAX_ENTRIES {
            q.pop_back();
        }
    }

    /// A snapshot of the recent rejections, newest first.
    pub fn recent(&self) -> Vec<BlockedConnection> {
        self.lock().iter().cloned().collect()
    }

    /// Drop all recorded entries (the panel's "Clear" button).
    pub fn clear(&self) {
        self.lock().clear();
    }

    fn lock(&self) -> std::sync::MutexGuard<'_, VecDeque<BlockedConnection>> {
        // A poisoned lock only means a prior panic while recording; the buffer
        // is still usable.
        self.entries
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
    }
}

/// Current time as Unix epoch milliseconds (UTC). Zero if the clock is before
/// the epoch (not reachable in practice).
fn now_unix_ms() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or(0)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::net::Ipv4Addr;

    fn ip(last: u8) -> IpAddr {
        IpAddr::V4(Ipv4Addr::new(192, 168, 1, last))
    }

    #[test]
    fn records_newest_first_with_reason_text() {
        let log = BlockedLog::default();
        log.record(ip(10), 5000, BlockReason::AddressNotAllowed);
        log.record(ip(11), 5001, BlockReason::PerIpCap);

        let recent = log.recent();
        assert_eq!(recent.len(), 2);
        // Newest first.
        assert_eq!(recent[0].ip, "192.168.1.11");
        assert_eq!(recent[0].port, 5001);
        assert_eq!(recent[0].reason, "Too many connections from this address");
        assert_eq!(recent[1].ip, "192.168.1.10");
        assert_eq!(recent[1].reason, "Address not allowed");
        assert!(recent[0].unix_ms > 0);
    }

    #[test]
    fn caps_at_max_entries_dropping_oldest() {
        let log = BlockedLog::default();
        for i in 0..(MAX_ENTRIES + 10) {
            log.record(ip(i as u8), 6000, BlockReason::PerClientCap);
        }
        let recent = log.recent();
        assert_eq!(recent.len(), MAX_ENTRIES);
        // The most recent record (i = MAX_ENTRIES + 9) is at the front; the
        // oldest 10 were evicted.
        assert_eq!(
            recent[0].ip,
            format!("192.168.1.{}", (MAX_ENTRIES + 9) as u8)
        );
        assert_eq!(recent[MAX_ENTRIES - 1].ip, format!("192.168.1.{}", 10u8));
    }

    #[test]
    fn clear_empties_the_log() {
        let log = BlockedLog::default();
        log.record(ip(1), 7000, BlockReason::AddressNotAllowed);
        log.clear();
        assert!(log.recent().is_empty());
    }

    #[test]
    fn recent_round_trips_as_named_msgpack() {
        // Locks the exact serialization `state::recent_blocked_bytes` uses: named
        // maps (to_vec_named) so the C# contractless resolver reads by field name.
        // `to_vec` (positional) would silently break the C# side.
        let log = BlockedLog::default();
        log.record(ip(5), 5555, BlockReason::AddressNotAllowed);
        let bytes = rmp_serde::to_vec_named(&log.recent()).unwrap();
        let back: Vec<BlockedConnection> = rmp_serde::from_slice(&bytes).unwrap();
        assert_eq!(back.len(), 1);
        assert_eq!(back[0].ip, "192.168.1.5");
        assert_eq!(back[0].port, 5555);
        assert_eq!(back[0].reason, "Address not allowed");
        assert!(back[0].unix_ms > 0);
    }
}
