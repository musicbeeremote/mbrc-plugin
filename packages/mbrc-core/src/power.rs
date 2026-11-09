//! Remote auto-shutdown (#108): a timer that stops playback, closes MusicBee,
//! sleeps the PC or shuts it down, and the permission the last three need.
//!
//! The permission is per device and independent of Party Mode. No role grants
//! it, and a device the user ticked keeps it whatever its role. Only a proven
//! identity can hold it, a V6 app verified by its `client_token` or a paired
//! browser, and the list is empty until the user ticks one.
//!
//! When the timer runs out on one of those three, the PC gets a last
//! [`FINAL_COUNTDOWN`] that the PC, or any client that may stop playback, can
//! still cancel. The timer lives in the
//! core, so it ends when MusicBee does.

use std::collections::HashMap;
use std::sync::{Mutex, MutexGuard};
use std::time::{Duration, Instant};

use mbrc_wire::v6::ErrorCode;
use redb::{Durability, ReadableTable};
use serde_json::{Value, json};
use tokio::sync::Notify;

use crate::providers::Providers;
use crate::server::commands_v6::{V6Error, req_i64, req_str};
use crate::server::permissions::Principal;
use crate::store::{Db, POWER_GRANTS};

pub const OPS: &[&str] = &["system_auto_shutdown", "system_set_auto_shutdown"];

/// The event sent whenever the timer is set, cancelled, enters its final
/// countdown or fires.
pub const CHANGED_EVENT: &str = "auto_shutdown_changed";

/// How long the PC is warned before MusicBee closes or the PC sleeps or shuts down.
pub const FINAL_COUNTDOWN: Duration = Duration::from_secs(60);

/// The longest timer a client may set, in minutes.
const MAX_MINUTES: u64 = 24 * 60;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PowerAction {
    Stop,
    Close,
    Sleep,
    Shutdown,
}

impl PowerAction {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Stop => "stop",
            Self::Close => "close",
            Self::Sleep => "sleep",
            Self::Shutdown => "shutdown",
        }
    }

    pub fn parse(name: &str) -> Option<Self> {
        [Self::Stop, Self::Close, Self::Sleep, Self::Shutdown]
            .into_iter()
            .find(|action| action.as_str() == name)
    }

    /// Whether only a device on the permission list may set it.
    pub fn needs_permission(self) -> bool {
        self != Self::Stop
    }
}

#[derive(Debug, Clone)]
struct Armed {
    action: PowerAction,
    fires_at: Instant,
    /// Whether this is the warning before the action rather than the timer.
    last: bool,
    by: Option<String>,
}

/// What the timer is doing, as clients are told.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Status {
    pub action: Option<PowerAction>,
    pub remaining: Duration,
    pub final_countdown: bool,
}

impl Status {
    pub fn to_json(&self) -> Value {
        json!({
            "active": self.action.is_some(),
            "action": self.action.map(PowerAction::as_str),
            "remaining_ms": u64::try_from(self.remaining.as_millis()).unwrap_or(u64::MAX),
            "final_countdown": self.final_countdown,
        })
    }
}

/// The one timer, which a later set replaces.
#[derive(Default)]
pub struct AutoShutdown {
    armed: Mutex<Option<Armed>>,
    changed: Notify,
}

impl AutoShutdown {
    /// Sets `action` to happen `after` from `now`, replacing any timer set before.
    pub fn set(
        &self,
        action: PowerAction,
        after: Duration,
        by: Option<String>,
        now: Instant,
    ) -> Status {
        *self.lock() = Some(Armed {
            action,
            fires_at: now + after,
            last: false,
            by,
        });
        self.changed.notify_one();
        self.status(now)
    }

    pub fn cancel(&self, now: Instant) -> Status {
        if self.lock().take().is_some() {
            self.changed.notify_one();
        }
        self.status(now)
    }

    /// Cancels the timer if the device under `key` set it, as it loses the permission.
    pub fn cancel_set_by(&self, key: &str) {
        let mut armed = self.lock();
        if armed.as_ref().and_then(|a| a.by.as_deref()) == Some(key) {
            *armed = None;
            self.changed.notify_one();
        }
    }

    pub fn status(&self, now: Instant) -> Status {
        match self.lock().as_ref() {
            Some(armed) => Status {
                action: Some(armed.action),
                remaining: armed.fires_at.saturating_duration_since(now),
                final_countdown: armed.last,
            },
            None => Status {
                action: None,
                remaining: Duration::ZERO,
                final_countdown: false,
            },
        }
    }

    pub fn deadline(&self) -> Option<Instant> {
        self.lock().as_ref().map(|armed| armed.fires_at)
    }

    /// The action due at `now`. One that needs the permission first gets its
    /// [`FINAL_COUNTDOWN`], so it is due only once that runs out.
    pub fn take_due(&self, now: Instant) -> Option<PowerAction> {
        let mut armed = self.lock();
        let due = armed.as_mut().filter(|a| a.fires_at <= now)?;
        if due.action.needs_permission() && !due.last {
            due.last = true;
            due.fires_at = now + FINAL_COUNTDOWN;
            return None;
        }
        armed.take().map(|a| a.action)
    }

    /// Waits for the next set or cancel.
    pub async fn changed(&self) {
        self.changed.notified().await;
    }

    fn lock(&self) -> MutexGuard<'_, Option<Armed>> {
        self.armed.lock().unwrap_or_else(|e| e.into_inner())
    }
}

/// The devices allowed to close MusicBee, sleep or shut down the PC, by
/// [`Principal::key`], with when each was allowed.
#[derive(Default)]
pub struct PowerGrants {
    granted: Mutex<HashMap<String, i64>>,
    store: Mutex<Option<Db>>,
}

impl PowerGrants {
    /// Attaches the store and reads back the devices allowed in an earlier run.
    pub fn open(&self, db: Db) {
        let stored = db
            .read(|txn| {
                let table = match txn.open_table(POWER_GRANTS) {
                    Ok(table) => table,
                    Err(_) => return Ok(Vec::new()),
                };
                let mut rows = Vec::new();
                for row in table.iter()? {
                    let (key, at) = row?;
                    rows.push((key.value().to_owned(), at.value()));
                }
                Ok(rows)
            })
            .unwrap_or_default();
        self.lock().extend(stored);
        *self.lock_store() = Some(db);
    }

    pub fn allows(&self, principal: &Principal) -> bool {
        can_hold(principal)
            && principal
                .key()
                .is_some_and(|key| self.lock().contains_key(&key))
    }

    /// Allows `principal`; false for one whose identity is not proven.
    pub fn grant(&self, principal: &Principal, now_unix_secs: i64) -> bool {
        let Some(key) = principal.key().filter(|_| can_hold(principal)) else {
            return false;
        };
        self.lock().insert(key.clone(), now_unix_secs);
        self.persist(|txn| {
            txn.open_table(POWER_GRANTS)?
                .insert(key.as_str(), now_unix_secs)?;
            Ok(())
        });
        true
    }

    /// Takes the permission away; false when `principal` did not have it.
    pub fn revoke(&self, principal: &Principal) -> bool {
        let Some(key) = principal.key() else {
            return false;
        };
        if self.lock().remove(&key).is_none() {
            return false;
        }
        self.persist(|txn| {
            txn.open_table(POWER_GRANTS)?.remove(key.as_str())?;
            Ok(())
        });
        true
    }

    /// Every allowed device's key, with when it was allowed.
    pub fn granted(&self) -> Vec<(String, i64)> {
        let mut all: Vec<(String, i64)> =
            self.lock().iter().map(|(k, at)| (k.clone(), *at)).collect();
        all.sort();
        all
    }

    fn persist(&self, f: impl FnOnce(&redb::WriteTransaction) -> Result<(), redb::Error>) {
        let db = self.lock_store().clone();
        if let Some(db) = db {
            db.write(Durability::Immediate, f);
        }
    }

    fn lock(&self) -> MutexGuard<'_, HashMap<String, i64>> {
        self.granted.lock().unwrap_or_else(|e| e.into_inner())
    }

    fn lock_store(&self) -> MutexGuard<'_, Option<Db>> {
        self.store.lock().unwrap_or_else(|e| e.into_inner())
    }
}

/// Whether `principal` is a proven identity, the only kind that can be allowed.
fn can_hold(principal: &Principal) -> bool {
    matches!(principal, Principal::App(_) | Principal::Browser(_))
}

/// The permission list and the timer it guards.
#[derive(Default)]
pub struct Power {
    pub grants: PowerGrants,
    pub timer: AutoShutdown,
}

impl std::fmt::Debug for Power {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Power")
            .field("granted", &self.grants.granted().len())
            .field("armed", &self.timer.deadline().is_some())
            .finish()
    }
}

impl Power {
    /// Takes the permission from `principal`, cancelling a timer it set.
    pub fn revoke(&self, principal: &Principal) -> bool {
        if !self.grants.revoke(principal) {
            return false;
        }
        if let Some(key) = principal.key() {
            self.timer.cancel_set_by(&key);
        }
        true
    }

    /// Takes the permission from every browser, as unpairing them all does.
    pub fn revoke_browsers(&self) {
        for (key, _) in self.grants.granted() {
            if let Some(browser @ Principal::Browser(_)) = Principal::from_key(&key) {
                self.revoke(&browser);
            }
        }
    }

    /// Answers a power op for `principal`; `None` for any other op.
    pub fn handle(
        &self,
        principal: &Principal,
        op: &str,
        data: &Value,
    ) -> Option<Result<Value, V6Error>> {
        let answer = match op {
            "system_auto_shutdown" => Ok(self.timer.status(Instant::now())),
            "system_set_auto_shutdown" => self.set(principal, data, Instant::now()),
            _ => return None,
        };
        Some(answer.map(|status| {
            let mut out = status.to_json();
            out["power_allowed"] = json!(self.grants.allows(principal));
            out
        }))
    }

    fn set(&self, principal: &Principal, data: &Value, now: Instant) -> Result<Status, V6Error> {
        let name = req_str(data, "action")?;
        if name == "cancel" {
            return Ok(self.timer.cancel(now));
        }
        let action = PowerAction::parse(name).ok_or_else(|| {
            V6Error::field(
                ErrorCode::InvalidField,
                "action",
                "action must be stop, close, sleep, shutdown or cancel",
            )
        })?;
        if action.needs_permission() && !self.grants.allows(principal) {
            return Err(V6Error::new(
                ErrorCode::Forbidden,
                "this device may not close MusicBee, sleep or shut down this PC; allow it under Access in MusicBee Remote's settings",
            ));
        }
        let minutes = u64::try_from(req_i64(data, "minutes")?)
            .ok()
            .filter(|m| (1..=MAX_MINUTES).contains(m))
            .ok_or_else(|| {
                V6Error::field(
                    ErrorCode::InvalidField,
                    "minutes",
                    format!("minutes must be a whole number from 1 to {MAX_MINUTES}"),
                )
            })?;
        Ok(self.timer.set(
            action,
            Duration::from_secs(minutes * 60),
            principal.key(),
            now,
        ))
    }
}

/// Carries out `action` now.
///
/// # Errors
/// MusicBee or Windows refused it.
pub fn execute(action: PowerAction, providers: &dyn Providers) -> Result<(), String> {
    match action {
        PowerAction::Stop => providers.stop(),
        PowerAction::Close => providers.close_musicbee(),
        PowerAction::Sleep => windows::sleep(),
        PowerAction::Shutdown => windows::shutdown(),
    }
}

#[cfg(windows)]
mod windows {
    use std::ptr::{null, null_mut};

    use windows_sys::Win32::Foundation::{
        CloseHandle, ERROR_NOT_ALL_ASSIGNED, GetLastError, HANDLE, LUID,
    };
    use windows_sys::Win32::Security::{
        AdjustTokenPrivileges, LUID_AND_ATTRIBUTES, LookupPrivilegeValueW, SE_PRIVILEGE_ENABLED,
        SE_SHUTDOWN_NAME, TOKEN_ADJUST_PRIVILEGES, TOKEN_PRIVILEGES, TOKEN_QUERY,
    };
    use windows_sys::Win32::System::Power::SetSuspendState;
    use windows_sys::Win32::System::Shutdown::{
        InitiateSystemShutdownExW, SHTDN_REASON_FLAG_PLANNED,
    };
    use windows_sys::Win32::System::Threading::{GetCurrentProcess, OpenProcessToken};

    fn last_error() -> u32 {
        // SAFETY: reads this thread's last error code; no arguments.
        unsafe { GetLastError() }
    }

    /// Enables the shutdown privilege, which sleep and shutdown both need.
    ///
    /// Judged by the call's own result, then by `ERROR_NOT_ALL_ASSIGNED`, which
    /// `AdjustTokenPrivileges` reports alongside success when the account does
    /// not hold the privilege at all.
    fn enable_shutdown_privilege() -> Result<(), String> {
        let mut token: HANDLE = null_mut();
        // SAFETY: the process pseudo-handle is always valid and `token` outlives the call.
        if unsafe {
            OpenProcessToken(
                GetCurrentProcess(),
                TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY,
                &mut token,
            )
        } == 0
        {
            return Err(format!("OpenProcessToken failed ({})", last_error()));
        }
        let mut luid = LUID {
            LowPart: 0,
            HighPart: 0,
        };
        // SAFETY: the name is a static null-terminated string and `luid` outlives the call.
        let found = unsafe { LookupPrivilegeValueW(null(), SE_SHUTDOWN_NAME, &mut luid) };
        let privileges = TOKEN_PRIVILEGES {
            PrivilegeCount: 1,
            Privileges: [LUID_AND_ATTRIBUTES {
                Luid: luid,
                Attributes: SE_PRIVILEGE_ENABLED,
            }],
        };
        // SAFETY: `token` is open with TOKEN_ADJUST_PRIVILEGES; no previous state is asked for.
        let adjusted = found != 0
            && unsafe { AdjustTokenPrivileges(token, 0, &privileges, 0, null_mut(), null_mut()) }
                != 0;
        let error = last_error();
        // SAFETY: `token` was opened above and is closed once.
        unsafe { CloseHandle(token) };
        if adjusted && error == ERROR_NOT_ALL_ASSIGNED {
            Err("this Windows account may not sleep or shut down the PC".into())
        } else if adjusted {
            Ok(())
        } else {
            Err(format!(
                "the shutdown privilege could not be enabled ({error})"
            ))
        }
    }

    pub fn sleep() -> Result<(), String> {
        enable_shutdown_privilege()?;
        // SAFETY: no pointers; sleep, not hibernate, not forced, wake events left on.
        if unsafe { SetSuspendState(false, false, false) } {
            Ok(())
        } else {
            Err(format!("Windows refused to sleep ({})", last_error()))
        }
    }

    pub fn shutdown() -> Result<(), String> {
        enable_shutdown_privilege()?;
        // SAFETY: null machine and message mean this PC and no message. Apps are
        // not forced closed, so one with unsaved work can hold the shutdown up.
        if unsafe { InitiateSystemShutdownExW(null(), null(), 0, 0, 0, SHTDN_REASON_FLAG_PLANNED) }
            != 0
        {
            Ok(())
        } else {
            Err(format!("Windows refused to shut down ({})", last_error()))
        }
    }
}

#[cfg(not(windows))]
mod windows {
    pub fn sleep() -> Result<(), String> {
        Err("sleep is only available on Windows".into())
    }

    pub fn shutdown() -> Result<(), String> {
        Err("shutdown is only available on Windows".into())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const MINUTE: Duration = Duration::from_secs(60);

    fn app(id: &str) -> Principal {
        Principal::App(id.into())
    }

    fn set(power: &Power, who: &Principal, action: &str, minutes: u64) -> Result<Value, V6Error> {
        power
            .handle(
                who,
                "system_set_auto_shutdown",
                &json!({ "action": action, "minutes": minutes }),
            )
            .unwrap()
    }

    #[test]
    fn no_device_may_close_sleep_or_shut_down_until_one_is_allowed() {
        let power = Power::default();
        for action in ["close", "sleep", "shutdown"] {
            let err = set(&power, &app("phone"), action, 5).unwrap_err();
            assert_eq!(err.code, ErrorCode::Forbidden, "{action}");
        }
        assert_eq!(power.timer.deadline(), None);

        assert!(power.grants.grant(&app("phone"), 1));
        let out = set(&power, &app("phone"), "sleep", 5).unwrap();
        assert_eq!(out["action"], "sleep");
        assert_eq!(out["power_allowed"], true);
        assert_eq!(
            set(&power, &app("tablet"), "sleep", 5).unwrap_err().code,
            ErrorCode::Forbidden
        );
    }

    #[test]
    fn anyone_may_stop_or_cancel() {
        let power = Power::default();
        assert_eq!(
            set(&power, &Principal::Anonymous, "stop", 30).unwrap()["action"],
            "stop"
        );
        let out = power
            .handle(
                &Principal::Anonymous,
                "system_set_auto_shutdown",
                &json!({ "action": "cancel" }),
            )
            .unwrap()
            .unwrap();
        assert_eq!(out["active"], false);
        assert_eq!(out["power_allowed"], false);
    }

    #[test]
    fn only_a_proven_identity_can_be_allowed() {
        let grants = PowerGrants::default();
        assert!(!grants.grant(&Principal::Anonymous, 1));
        assert!(!grants.grant(&Principal::LegacyDevice("android-1.6".into()), 1));
        assert!(grants.grant(&Principal::Browser("desk".into()), 1));
        assert!(grants.allows(&Principal::Browser("desk".into())));
        assert!(
            !grants.allows(&Principal::App("desk".into())),
            "kinds are keyed apart"
        );
    }

    #[test]
    fn a_timer_on_the_list_gets_a_final_countdown_and_stop_does_not() {
        let timer = AutoShutdown::default();
        let start = Instant::now();
        timer.set(PowerAction::Shutdown, MINUTE, None, start);
        assert_eq!(timer.take_due(start + MINUTE / 2), None, "not yet");
        assert_eq!(timer.take_due(start + MINUTE), None, "the warning starts");
        let warning = timer.status(start + MINUTE);
        assert!(warning.final_countdown);
        assert_eq!(warning.remaining, FINAL_COUNTDOWN);
        assert_eq!(
            timer.take_due(start + MINUTE + FINAL_COUNTDOWN),
            Some(PowerAction::Shutdown)
        );
        assert_eq!(timer.status(start).action, None);

        timer.set(PowerAction::Stop, MINUTE, None, start);
        assert_eq!(timer.take_due(start + MINUTE), Some(PowerAction::Stop));
    }

    #[test]
    fn a_cancel_during_the_final_countdown_keeps_the_pc_on() {
        let timer = AutoShutdown::default();
        let start = Instant::now();
        timer.set(PowerAction::Sleep, MINUTE, None, start);
        assert_eq!(timer.take_due(start + MINUTE), None);
        timer.cancel(start + MINUTE);
        assert_eq!(timer.take_due(start + MINUTE + FINAL_COUNTDOWN), None);
    }

    #[test]
    fn revoking_a_device_cancels_the_timer_it_set_and_only_that() {
        let power = Power::default();
        power.grants.grant(&app("phone"), 1);
        power.grants.grant(&app("tablet"), 1);
        set(&power, &app("phone"), "shutdown", 10).unwrap();

        assert!(power.revoke(&app("tablet")));
        assert!(
            power.timer.deadline().is_some(),
            "another device's timer stays"
        );
        assert!(power.revoke(&app("phone")));
        assert_eq!(power.timer.deadline(), None);
        assert!(!power.grants.allows(&app("phone")));
    }

    #[test]
    fn revoking_a_device_that_held_nothing_leaves_its_stop_timer() {
        let power = Power::default();
        set(&power, &app("phone"), "stop", 10).unwrap();
        assert!(!power.revoke(&app("phone")));
        assert!(power.timer.deadline().is_some());
    }

    #[test]
    fn unpairing_every_browser_takes_their_permission_and_timer_but_not_apps() {
        let power = Power::default();
        let desk = Principal::Browser("desk".into());
        power.grants.grant(&desk, 1);
        power.grants.grant(&app("phone"), 1);
        set(&power, &desk, "sleep", 10).unwrap();

        power.revoke_browsers();
        assert!(!power.grants.allows(&desk));
        assert!(power.grants.allows(&app("phone")));
        assert_eq!(power.timer.deadline(), None);
    }

    #[test]
    fn minutes_and_action_are_checked() {
        let power = Power::default();
        let bad = |data: Value| {
            power
                .handle(&Principal::Anonymous, "system_set_auto_shutdown", &data)
                .unwrap()
                .unwrap_err()
        };
        assert_eq!(
            bad(json!({ "action": "stop" })).code,
            ErrorCode::MissingField
        );
        assert_eq!(
            bad(json!({ "action": "stop", "minutes": 0 })).code,
            ErrorCode::InvalidField
        );
        assert_eq!(
            bad(json!({ "action": "stop", "minutes": MAX_MINUTES + 1 })).code,
            ErrorCode::InvalidField
        );
        assert_eq!(
            bad(json!({ "action": "reboot", "minutes": 5 }))
                .field
                .as_deref(),
            Some("action")
        );
        assert!(
            power
                .handle(&Principal::Anonymous, "player_stop", &json!({}))
                .is_none()
        );
    }

    #[test]
    fn the_list_survives_a_restart() {
        let (_dir, db) = crate::cover::test_storage("power-grants");
        let grants = PowerGrants::default();
        grants.open(db.clone());
        grants.grant(&app("phone"), 7);
        grants.grant(&app("tablet"), 8);
        grants.revoke(&app("tablet"));

        let reopened = PowerGrants::default();
        reopened.open(db);
        assert!(reopened.allows(&app("phone")));
        assert!(!reopened.allows(&app("tablet")));
        assert_eq!(reopened.granted(), [(app("phone").key().unwrap(), 7)]);
    }
}
