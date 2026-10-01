//! The Party Mode gate: the switch, the one check every transport calls, and
//! the log of recent refusals the panel shows.
//!
//! Refusals are kept in memory only, like the blocked-connections log. Allowed
//! requests are not logged: at a party that is every tap.
//!
//! Every change that can alter what a client may do (the switch, a role) bumps
//! one counter. Each V6 client with an event stream watches it, recomputes its
//! own [`permissions`](PartyMode::permissions), and is sent `permissions_changed`
//! only when those differ from what it was last told.

use std::collections::{HashMap, VecDeque};
use std::net::IpAddr;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{SystemTime, UNIX_EPOCH};

use serde_json::{Value, json};
use tokio::sync::{mpsc, watch};

use super::{Action, Capability, Principal, Role, Roles};
use crate::store::Db;

/// The event a client is sent when what it may do changes.
pub const PERMISSIONS_CHANGED: &str = "permissions_changed";

/// How many recent refusals to keep. Older entries are dropped.
const MAX_REFUSALS: usize = 50;

/// When and where a device was last seen.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct Sighting {
    /// Unix seconds, zero for a device not seen since start.
    pub at: i64,
    pub from: Option<IpAddr>,
}

/// How many Android devices seen this session the panel can list.
const MAX_LEGACY_SEEN: usize = 200;

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
#[derive(Debug)]
pub struct PartyMode {
    enabled: AtomicBool,
    refusals: Mutex<VecDeque<Refusal>>,
    roles: Roles,
    /// Bumped by every change that can alter a client's permissions.
    changes: watch::Sender<u64>,
    /// Android 1.6 devices seen since start, by `client_id`.
    legacy_seen: Mutex<HashMap<String, Sighting>>,
}

impl Default for PartyMode {
    fn default() -> Self {
        Self {
            enabled: AtomicBool::new(false),
            refusals: Mutex::default(),
            roles: Roles::default(),
            changes: watch::Sender::new(0),
            legacy_seen: Mutex::default(),
        }
    }
}

impl PartyMode {
    /// Attaches the store the roles live in.
    pub fn open(&self, db: Db) {
        self.roles.open(db);
    }

    pub fn is_enabled(&self) -> bool {
        self.enabled.load(Ordering::Relaxed)
    }

    pub fn set_enabled(&self, enabled: bool) {
        if self.enabled.swap(enabled, Ordering::Relaxed) != enabled {
            tracing::info!(enabled, "party mode switched");
            self.changed();
        }
    }

    /// The role `principal` holds while Party Mode is on.
    pub fn role_of(&self, principal: &Principal) -> Role {
        self.roles.role_of(principal)
    }

    /// Gives `principal` a role. False for a principal that cannot hold one.
    pub fn assign_role(&self, principal: &Principal, role: Role) -> bool {
        let assigned = self.roles.assign(principal, role);
        if assigned {
            self.changed();
        }
        assigned
    }

    /// Returns `principal` to the default role.
    pub fn unassign_role(&self, principal: &Principal) {
        self.roles.unassign(principal);
        self.changed();
    }

    /// Returns every paired browser to the default role.
    pub fn unassign_browsers(&self) {
        self.roles.unassign_browsers();
        self.changed();
    }

    /// What `principal` may do, in the shape the handshake, the capabilities
    /// route and `permissions_changed` all carry.
    ///
    /// With Party Mode off every client is told it is the host with every
    /// capability, which is what it can do.
    pub fn permissions(&self, principal: &Principal) -> Value {
        if !self.is_enabled() {
            return json!({
                "party_mode": false,
                "role": Role::Host.as_str(),
                "allowed": capability_names(Capability::ALL),
            });
        }
        let role = self.role_of(principal);
        let mut permissions = json!({
            "party_mode": true,
            "role": role.as_str(),
            "allowed": capability_names(role.capabilities()),
        });
        if let Some(max) = role.max_tracks_per_add() {
            permissions["max_tracks_per_add"] = json!(max);
        }
        permissions
    }

    /// Sends `permissions_changed` frames down `tx` whenever `principal`'s
    /// permissions stop matching `told`, until `tx` closes.
    ///
    /// Checks once straight away, so a change that landed between the
    /// handshake and this call is not lost.
    pub fn watch(
        self: &Arc<Self>,
        principal: Principal,
        told: Value,
        tx: mpsc::UnboundedSender<String>,
    ) {
        let gate = Arc::clone(self);
        let mut changes = self.changes.subscribe();
        tokio::spawn(async move {
            let mut told = told;
            loop {
                let now = gate.permissions(&principal);
                if now != told {
                    let frame = mbrc_wire::v6::event(PERMISSIONS_CHANGED, now.clone());
                    if tx.send(frame).is_err() {
                        break;
                    }
                    told = now;
                }
                tokio::select! {
                    changed = changes.changed() => if changed.is_err() { break },
                    () = tx.closed() => break,
                }
            }
        });
    }

    /// Records an Android 1.6 client, so the panel can offer it a role.
    ///
    /// Kept in memory only: a device that holds a role is listed from the roles
    /// table anyway, and one that does not is only worth listing while it is around.
    pub fn saw_legacy_device(&self, client_id: &str, from: IpAddr) {
        let now = now_unix_ms() / 1000;
        let mut seen = self.legacy_seen.lock().unwrap_or_else(|p| p.into_inner());
        if seen.len() >= MAX_LEGACY_SEEN
            && !seen.contains_key(client_id)
            && let Some(oldest) = seen
                .iter()
                .min_by_key(|(_, seen)| seen.at)
                .map(|(id, _)| id.clone())
        {
            seen.remove(&oldest);
        }
        seen.insert(
            client_id.to_owned(),
            Sighting {
                at: now,
                from: Some(from),
            },
        );
    }

    /// Every Android 1.6 device worth listing: seen this session, or holding a role.
    pub fn legacy_devices(&self) -> Vec<(String, Sighting)> {
        let mut devices: HashMap<String, Sighting> = self
            .legacy_seen
            .lock()
            .unwrap_or_else(|p| p.into_inner())
            .clone();
        for id in self.roles.legacy_devices() {
            devices.entry(id).or_default();
        }
        devices.into_iter().collect()
    }

    fn changed(&self) {
        self.changes.send_modify(|n| *n = n.wrapping_add(1));
    }

    /// Whether `principal` may carry out `op`, which the map classified as `action`.
    ///
    /// Everything passes while Party Mode is off. The role is looked up on every
    /// call, so a role the host changes applies to the next request. An op the
    /// map does not know passes only for the host. A refusal is logged and recorded.
    pub fn check(
        &self,
        principal: &Principal,
        op: &str,
        action: Option<Action>,
        client: &str,
    ) -> Result<(), Refusal> {
        if !self.is_enabled() {
            return Ok(());
        }
        let role = self.roles.role_of(principal);
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

fn capability_names(capabilities: &[Capability]) -> Vec<&'static str> {
    capabilities.iter().map(|c| c.as_str()).collect()
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

    /// A gate that is on, with one principal of each role that holds one.
    fn with_role(role: Role) -> (PartyMode, Principal) {
        let gate = on();
        let who = Principal::App(format!("{role:?}"));
        gate.assign_role(&who, role);
        (gate, who)
    }
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
        assert!(
            gate.check(&Principal::Anonymous, "player_next", skip, "c")
                .is_ok()
        );
        assert!(
            gate.check(&Principal::Anonymous, "mystery", None, "c")
                .is_ok()
        );
        assert!(gate.recent_refusals().is_empty());
    }

    #[test]
    fn a_refusal_names_the_capability_and_is_recorded() {
        let gate = on();
        let skip = Some(Action::Change(Capability::Playback));
        let refusal = gate
            .check(&Principal::Anonymous, "player_next", skip, "phone")
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
                &Principal::Anonymous,
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
        let (gate, host) = with_role(Role::Host);
        let dj = Principal::App("dj".into());
        gate.assign_role(&dj, Role::Dj);
        assert!(gate.check(&host, "mystery", None, "c").is_ok());
        assert!(gate.check(&dj, "mystery", None, "c").is_err());
    }

    #[test]
    fn an_android_device_with_a_role_is_listed_before_it_is_seen() {
        let gate = PartyMode::default();
        gate.saw_legacy_device("seen", IpAddr::from([10, 0, 0, 7]));
        gate.assign_role(&Principal::LegacyDevice("promoted".into()), Role::Dj);
        let mut devices = gate.legacy_devices();
        devices.sort_by(|a, b| a.0.cmp(&b.0));
        assert_eq!(devices.len(), 2);
        assert_eq!(devices[0], ("promoted".to_owned(), Sighting::default()));
        assert_eq!(devices[1].0, "seen");
        assert!(devices[1].1.at > 0);
        assert_eq!(devices[1].1.from, Some(IpAddr::from([10, 0, 0, 7])));
    }

    #[test]
    fn the_android_devices_seen_are_bounded() {
        let gate = PartyMode::default();
        for n in 0..MAX_LEGACY_SEEN + 5 {
            gate.saw_legacy_device(&format!("device-{n}"), IpAddr::from([10, 0, 0, 1]));
        }
        assert_eq!(gate.legacy_devices().len(), MAX_LEGACY_SEEN);
    }

    #[test]
    fn with_party_mode_off_everyone_is_told_they_are_the_host() {
        let permissions = PartyMode::default().permissions(&Principal::Anonymous);
        assert_eq!(permissions["party_mode"], false);
        assert_eq!(permissions["role"], "host");
        assert_eq!(
            permissions["allowed"].as_array().unwrap().len(),
            Capability::ALL.len()
        );
        assert!(permissions.get("max_tracks_per_add").is_none());
    }

    #[test]
    fn a_guest_is_told_its_one_capability_and_its_track_limit() {
        let permissions = on().permissions(&Principal::Anonymous);
        assert_eq!(
            permissions,
            json!({
                "party_mode": true,
                "role": "guest",
                "allowed": ["queue_add"],
                "max_tracks_per_add": 1,
            })
        );
    }

    #[test]
    fn a_dj_has_no_track_limit_to_be_told() {
        let (gate, dj) = with_role(Role::Dj);
        let permissions = gate.permissions(&dj);
        assert_eq!(permissions["role"], "dj");
        assert!(permissions.get("max_tracks_per_add").is_none());
    }

    /// The next frame the watcher sends, or `None` if it sends nothing promptly.
    async fn next_frame(rx: &mut mpsc::UnboundedReceiver<String>) -> Option<Value> {
        let frame = tokio::time::timeout(std::time::Duration::from_millis(200), rx.recv())
            .await
            .ok()??;
        Some(serde_json::from_str(&frame).unwrap())
    }

    #[tokio::test]
    async fn switching_party_mode_on_tells_a_watching_client() {
        let gate = Arc::new(PartyMode::default());
        let (tx, mut rx) = mpsc::unbounded_channel();
        gate.watch(
            Principal::Anonymous,
            gate.permissions(&Principal::Anonymous),
            tx,
        );
        gate.set_enabled(true);
        let event = next_frame(&mut rx).await.expect("an event");
        assert_eq!(event["event"], PERMISSIONS_CHANGED);
        assert_eq!(event["data"]["role"], "guest");
    }

    #[tokio::test]
    async fn a_change_to_someone_else_tells_this_client_nothing() {
        let gate = Arc::new(on());
        let me = Principal::App("me".into());
        let (tx, mut rx) = mpsc::unbounded_channel();
        gate.watch(me.clone(), gate.permissions(&me), tx);
        gate.assign_role(&Principal::App("them".into()), Role::Dj);
        assert_eq!(next_frame(&mut rx).await, None);

        gate.assign_role(&me, Role::Dj);
        let event = next_frame(&mut rx).await.expect("an event");
        assert_eq!(event["data"]["role"], "dj");
    }

    #[tokio::test]
    async fn a_change_before_the_watch_began_is_caught_up() {
        let gate = Arc::new(PartyMode::default());
        let told = gate.permissions(&Principal::Anonymous);
        gate.set_enabled(true);
        let (tx, mut rx) = mpsc::unbounded_channel();
        gate.watch(Principal::Anonymous, told, tx);
        assert!(next_frame(&mut rx).await.is_some());
    }

    #[test]
    fn a_role_change_applies_to_the_next_request() {
        let (gate, who) = with_role(Role::Listener);
        let skip = Some(Action::Change(Capability::Playback));
        assert!(gate.check(&who, "player_next", skip, "c").is_err());
        gate.assign_role(&who, Role::Dj);
        assert!(gate.check(&who, "player_next", skip, "c").is_ok());
    }

    #[test]
    fn the_log_keeps_only_the_most_recent_refusals() {
        let gate = on();
        for n in 0..MAX_REFUSALS + 5 {
            let _ = gate.check(&Principal::Anonymous, &format!("op{n}"), None, "c");
        }
        let log = gate.recent_refusals();
        assert_eq!(log.len(), MAX_REFUSALS);
        assert_eq!(log[0].op, format!("op{}", MAX_REFUSALS + 4));
    }
}
