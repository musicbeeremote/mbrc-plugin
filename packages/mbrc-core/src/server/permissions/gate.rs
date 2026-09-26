//! The Party Mode gate: the switch, the one check every transport calls, and
//! the log of recent refusals the panel shows.
//!
//! Refusals are kept in memory only, like the blocked-connections log. Allowed
//! requests are not logged: at a party that is every tap.

use std::collections::VecDeque;
use std::sync::Mutex;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::{SystemTime, UNIX_EPOCH};

use super::{Action, Capability, Role};

/// How many recent refusals to keep. Older entries are dropped.
const MAX_REFUSALS: usize = 50;

/// One refused request.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Refusal {
    pub unix_ms: i64,
    /// Who asked, as far as the transport can tell.
    pub client: String,
    /// The V6 op or V4 context.
    pub op: String,
    /// The capability the request needed; `None` for an op the map does not know.
    pub capability: Option<Capability>,
    /// What the client is told.
    pub message: String,
}

/// Party Mode's runtime state, shared by every connection.
#[derive(Debug, Default)]
pub struct PartyMode {
    enabled: AtomicBool,
    refusals: Mutex<VecDeque<Refusal>>,
}

impl PartyMode {
    pub fn is_enabled(&self) -> bool {
        self.enabled.load(Ordering::Relaxed)
    }

    pub fn set_enabled(&self, enabled: bool) {
        self.enabled.store(enabled, Ordering::Relaxed);
    }

    /// Whether `role` may carry out `op`, which the map classified as `action`.
    ///
    /// Everything passes while Party Mode is off. An op the map does not know
    /// passes only for the host. A refusal is logged and recorded before it is returned.
    pub fn check(
        &self,
        role: Role,
        op: &str,
        action: Option<Action>,
        client: &str,
    ) -> Result<(), Refusal> {
        if !self.is_enabled() {
            return Ok(());
        }
        let (permitted, capability) = match action {
            Some(action) => (role.permits(&action), action.capability()),
            None => (role == Role::Host, None),
        };
        if permitted {
            return Ok(());
        }
        let refusal = Refusal {
            unix_ms: now_unix_ms(),
            client: client.to_owned(),
            op: op.to_owned(),
            capability,
            message: refusal_message(role, op, action),
        };
        tracing::info!(
            client,
            op,
            role = ?role,
            capability = capability.map_or("unmapped", Capability::as_str),
            "party mode refused a request"
        );
        let mut log = self.lock();
        log.push_front(refusal.clone());
        log.truncate(MAX_REFUSALS);
        Err(refusal)
    }

    /// The recent refusals, newest first.
    pub fn recent_refusals(&self) -> Vec<Refusal> {
        self.lock().iter().cloned().collect()
    }

    pub fn clear_refusals(&self) {
        self.lock().clear();
    }

    fn lock(&self) -> std::sync::MutexGuard<'_, VecDeque<Refusal>> {
        self.refusals.lock().unwrap_or_else(|p| p.into_inner())
    }
}

fn refusal_message(role: Role, op: &str, action: Option<Action>) -> String {
    match action {
        None => format!("`{op}` is not available in Party Mode"),
        Some(Action::Queue { capability, .. }) if role.capabilities().contains(&capability) => {
            let max = role.max_tracks_per_add().unwrap_or(0);
            format!("this client may add at most {max} track per request, to the end of the queue")
        }
        Some(action) => format!(
            "`{op}` needs the `{}` permission, which this client does not have",
            action.capability().map_or("", Capability::as_str)
        ),
    }
}

fn now_unix_ms() -> i64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as i64)
        .unwrap_or(0)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::QueueType;

    fn on() -> PartyMode {
        let gate = PartyMode::default();
        gate.set_enabled(true);
        gate
    }

    #[test]
    fn everything_passes_while_party_mode_is_off() {
        let gate = PartyMode::default();
        let skip = Some(Action::Change(Capability::Playback));
        assert!(gate.check(Role::Listener, "player_next", skip, "c").is_ok());
        assert!(gate.check(Role::Guest, "mystery", None, "c").is_ok());
        assert!(gate.recent_refusals().is_empty());
    }

    #[test]
    fn a_refusal_names_the_capability_and_is_recorded() {
        let gate = on();
        let skip = Some(Action::Change(Capability::Playback));
        let refusal = gate
            .check(Role::Guest, "player_next", skip, "phone")
            .unwrap_err();
        assert_eq!(refusal.capability, Some(Capability::Playback));
        assert!(
            refusal.message.contains("`playback`"),
            "{}",
            refusal.message
        );
        assert_eq!(gate.recent_refusals(), vec![refusal]);
    }

    #[test]
    fn a_guest_over_the_track_limit_is_told_the_limit() {
        let refusal = on()
            .check(
                Role::Guest,
                "q",
                Some(Action::queue(QueueType::Last, Some(3))),
                "c",
            )
            .unwrap_err();
        assert!(
            refusal.message.contains("at most 1 track"),
            "{}",
            refusal.message
        );
    }

    #[test]
    fn an_unmapped_op_passes_only_for_the_host() {
        let gate = on();
        assert!(gate.check(Role::Host, "mystery", None, "c").is_ok());
        assert!(gate.check(Role::Dj, "mystery", None, "c").is_err());
    }

    #[test]
    fn the_log_keeps_only_the_most_recent_refusals() {
        let gate = on();
        for n in 0..MAX_REFUSALS + 5 {
            let _ = gate.check(Role::Listener, &format!("op{n}"), None, "c");
        }
        let log = gate.recent_refusals();
        assert_eq!(log.len(), MAX_REFUSALS);
        assert_eq!(log[0].op, format!("op{}", MAX_REFUSALS + 4));
    }
}
