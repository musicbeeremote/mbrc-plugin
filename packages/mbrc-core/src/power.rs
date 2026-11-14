//! Remote auto-shutdown (#108): a timer that stops playback, closes MusicBee,
//! sleeps the PC or shuts it down, and the permission the last three need.
//!
//! The permission is per device and independent of Party Mode. No role grants
//! it, and a device the user ticked keeps it whatever its role. Only a proven
//! identity can hold it, a V6 app verified by its `client_token` or a paired
//! browser, and the list is empty until the user ticks one.
//!
//! Once one of those three is set, only a device holding the permission may
//! cancel or replace it, and the warning on the PC can always cancel it. When it
//! runs out the PC gets a last [`FINAL_COUNTDOWN`]. The timer lives in the
//! core, so it ends when MusicBee does.
//!
//! Two [`Options`] mirror MusicBee's own auto-shutdown: let the playing track
//! finish, and fade the volume out before playback stops.

use std::collections::HashMap;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Mutex, MutexGuard};
use std::time::{Duration, Instant};

use mbrc_wire::v6::ErrorCode;
use redb::{Durability, ReadableTable};
use serde_json::{Value, json};
use tokio::sync::Notify;

use crate::protocol::messages::PlayState;
use crate::providers::Providers;
use crate::server::commands_v6::{V6Error, opt_bool, req_i64, req_str};
use crate::server::permissions::Principal;
use crate::store::{Db, POWER_GRANTS};

pub const OPS: &[&str] = &["system_auto_shutdown", "system_set_auto_shutdown"];

/// The event sent whenever the timer is set, cancelled, enters its final
/// countdown or fires.
pub const CHANGED_EVENT: &str = "auto_shutdown_changed";

/// How long the PC is warned before MusicBee closes or the PC sleeps or shuts down.
pub const FINAL_COUNTDOWN: Duration = Duration::from_secs(60);

/// How long `fade_out` takes to bring the volume down before playback stops.
pub const FADE_OUT: Duration = Duration::from_secs(30);

/// How often the volume steps down during a fade.
const FADE_STEP: Duration = Duration::from_millis(500);

/// How much of the last track may still be left when the wait for it ends:
/// more means a seek or skip, and the wait goes on.
const TRACK_END_SLACK: Duration = Duration::from_secs(1);

/// The longest timer a client may set, in minutes.
const MAX_MINUTES: u64 = 24 * 60;

const NOT_ALLOWED: &str = "this device may not close MusicBee, sleep or shut down this PC; allow it under Access in MusicBee Remote's settings";

const GUARDED: &str = "only a device allowed to close MusicBee, sleep or shut down this PC may cancel or replace this timer";

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

/// What a timer does on its way to the action, as the request asks.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct Options {
    /// Lower the volume over the last [`FADE_OUT`], then stop playback and put
    /// the volume back.
    pub fade_out: bool,
    /// Let the track playing when the timer runs out end before the action.
    pub complete_last_track: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Phase {
    /// The minutes the client asked for.
    Timer,
    /// The rest of the track that was playing when the minutes ran out.
    LastTrack,
    /// The warning on the PC before close, sleep or shutdown.
    Warning,
}

#[derive(Debug, Clone)]
struct Armed {
    id: u64,
    action: PowerAction,
    options: Options,
    phase: Phase,
    started: Instant,
    fires_at: Instant,
    by: Option<String>,
    undo: Undo,
}

impl Armed {
    /// Whether this phase ends with the fade: the one that ends playback.
    fn fades(&self) -> bool {
        self.options.fade_out
            && match self.phase {
                Phase::Timer => !self.options.complete_last_track,
                Phase::LastTrack => true,
                Phase::Warning => false,
            }
    }

    fn fade_starts(&self) -> Instant {
        self.fires_at - FADE_OUT.min(self.fires_at.saturating_duration_since(self.started))
    }

    /// The share of the volume before the fade to play at `now`, while it runs.
    fn fade_level(&self, now: Instant) -> Option<f64> {
        let starts = self.fade_starts();
        if !self.fades() || now < starts || now >= self.fires_at {
            return None;
        }
        let left = self.fires_at.saturating_duration_since(now).as_secs_f64();
        Some(left / self.fires_at.duration_since(starts).as_secs_f64())
    }
}

/// What a timer changed in MusicBee, to be put back once it is gone.
#[derive(Debug, Default, Clone, Copy, PartialEq, Eq)]
struct Undo {
    /// The volume before the fade.
    volume: Option<i32>,
    /// Whether the timer turned MusicBee's stop-after-current on.
    stop_after_current: bool,
}

impl Undo {
    fn merge(&mut self, other: Undo) {
        self.volume = self.volume.or(other.volume);
        self.stop_after_current |= other.stop_after_current;
    }

    fn apply(self, player: &dyn Playback) {
        if self.stop_after_current {
            player.set_stop_after_current(false);
        }
        if let Some(volume) = self.volume {
            player.set_volume(volume);
        }
    }
}

/// What the timer is doing, as clients are told.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Status {
    pub action: Option<PowerAction>,
    pub options: Options,
    pub remaining: Duration,
    pub final_countdown: bool,
    pub waiting_for_track: bool,
}

impl Status {
    pub fn to_json(&self) -> Value {
        json!({
            "active": self.action.is_some(),
            "action": self.action.map(PowerAction::as_str),
            "remaining_ms": u64::try_from(self.remaining.as_millis()).unwrap_or(u64::MAX),
            "final_countdown": self.final_countdown,
            "waiting_for_track": self.waiting_for_track,
            "fade_out": self.options.fade_out,
            "complete_last_track": self.options.complete_last_track,
        })
    }
}

/// What MusicBee is playing, as far as the timer needs to know.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Playing {
    pub volume: i32,
    pub track_left: Duration,
    pub stop_after_current: bool,
}

/// The player calls the timer makes; [`HostPlayback`] sends them to MusicBee.
pub trait Playback {
    /// `None` while MusicBee is paused, stopped or cannot say.
    fn playing(&self) -> Option<Playing>;
    /// [`Playing::volume`] alone, without asking where the track is.
    fn playing_volume(&self) -> Option<i32>;
    fn set_volume(&self, volume: i32);
    fn stop(&self);
    fn set_stop_after_current(&self, on: bool);
}

/// [`Playback`] through the MusicBee API; a refused call is logged and skipped.
pub struct HostPlayback<'a>(pub &'a dyn Providers);

impl HostPlayback<'_> {
    fn log(call: &str, result: Result<(), String>) {
        if let Err(error) = result {
            tracing::warn!(call, %error, "auto-shutdown: MusicBee refused a player call");
        }
    }
}

impl Playback for HostPlayback<'_> {
    fn playing(&self) -> Option<Playing> {
        let state = self.0.player_state().ok()?;
        if state.play_state != PlayState::Playing {
            return None;
        }
        let position = self.0.playback_position().ok()?;
        let left = u64::try_from(position.total.saturating_sub(position.current)).unwrap_or(0);
        Some(Playing {
            volume: state.volume,
            track_left: Duration::from_millis(left),
            stop_after_current: state.stop_after_current,
        })
    }

    fn playing_volume(&self) -> Option<i32> {
        let state = self.0.player_state().ok()?;
        (state.play_state == PlayState::Playing).then_some(state.volume)
    }

    fn set_volume(&self, volume: i32) {
        Self::log("set_volume", self.0.set_volume(volume));
    }

    fn stop(&self) {
        Self::log("stop", self.0.stop());
    }

    fn set_stop_after_current(&self, on: bool) {
        Self::log("set_stop_after_current", self.0.set_stop_after_current(on));
    }
}

/// What one [`AutoShutdown::tick`] did.
#[derive(Debug, Default, Clone, Copy, PartialEq, Eq)]
pub struct Tick {
    /// The action now due, for the caller to carry out.
    pub fired: Option<PowerAction>,
    /// Whether the status moved on, so clients are told.
    pub changed: bool,
}

/// A timer to set: what, when, who asked and how.
#[derive(Debug, Clone)]
pub struct Plan {
    pub action: PowerAction,
    pub after: Duration,
    pub by: Option<String>,
    pub options: Options,
}

/// The one timer, which a later set replaces.
///
/// The player calls a timer makes, the fade and stop-after-current, happen in
/// [`AutoShutdown::tick`] and never under the lock. A timer cancelled or
/// replaced leaves what it changed for the next tick to put back.
#[derive(Default)]
pub struct AutoShutdown {
    armed: Mutex<Option<Armed>>,
    undo: Mutex<Undo>,
    /// Held across a tick's player calls, so a hand-back never interleaves.
    player_calls: Mutex<()>,
    next_id: AtomicU64,
    changed: Notify,
}

impl AutoShutdown {
    /// Sets `plan` from `now`, replacing any timer set before.
    #[cfg(test)]
    pub fn set(&self, plan: Plan, now: Instant) -> Status {
        self.replace(Some(plan), true, now)
            .unwrap_or_else(|| self.status(now))
    }

    /// Sets `next`, or cancels with `None`; `None` back when a timer that needs
    /// the permission is armed and `allowed` is false.
    fn replace(&self, next: Option<Plan>, allowed: bool, now: Instant) -> Option<Status> {
        let armed = self.lock();
        if !allowed && armed.as_ref().is_some_and(|a| a.action.needs_permission()) {
            return None;
        }
        let next = next.map(|plan| Armed {
            id: self.next_id.fetch_add(1, Ordering::Relaxed),
            action: plan.action,
            options: plan.options,
            phase: Phase::Timer,
            started: now,
            fires_at: now + plan.after,
            by: plan.by,
            undo: Undo::default(),
        });
        self.swap(armed, next);
        Some(self.status(now))
    }

    pub fn cancel(&self, now: Instant) -> Status {
        self.swap(self.lock(), None);
        self.status(now)
    }

    /// Cancels the timer if the device under `key` set it, as it loses the permission.
    pub fn cancel_set_by(&self, key: &str) {
        let armed = self.lock();
        if armed.as_ref().and_then(|a| a.by.as_deref()) == Some(key) {
            self.swap(armed, None);
        }
    }

    /// Puts `next` in place of the timer held under `armed`, leaving what the
    /// old one changed in MusicBee for the next tick to put back.
    fn swap(&self, mut armed: MutexGuard<'_, Option<Armed>>, next: Option<Armed>) {
        let old = std::mem::replace(&mut *armed, next);
        let changed = old.is_some() || armed.is_some();
        drop(armed);
        if let Some(old) = old {
            self.lock_undo().merge(old.undo);
        }
        if changed {
            self.changed.notify_one();
        }
    }

    pub fn status(&self, now: Instant) -> Status {
        match self.lock().as_ref() {
            Some(armed) => Status {
                action: Some(armed.action),
                options: armed.options,
                remaining: armed.fires_at.saturating_duration_since(now),
                final_countdown: armed.phase == Phase::Warning,
                waiting_for_track: armed.phase == Phase::LastTrack,
            },
            None => Status {
                action: None,
                options: Options::default(),
                remaining: Duration::ZERO,
                final_countdown: false,
                waiting_for_track: false,
            },
        }
    }

    pub fn deadline(&self) -> Option<Instant> {
        self.lock().as_ref().map(|armed| armed.fires_at)
    }

    /// When [`AutoShutdown::tick`] next has work: the next fade step, the
    /// fade's start, or the end of the phase.
    pub fn next_wake(&self, now: Instant) -> Option<Instant> {
        let armed = self.lock();
        let armed = armed.as_ref()?;
        Some(if armed.fade_level(now).is_some() {
            (now + FADE_STEP).min(armed.fires_at)
        } else if armed.fades() && now < armed.fade_starts() {
            armed.fade_starts()
        } else {
            armed.fires_at
        })
    }

    /// Moves the timer on at `now`: puts back what a cancelled timer changed,
    /// steps the fade, and ends a phase that is due.
    ///
    /// When the minutes run out with `complete_last_track`, the timer waits out
    /// the playing track, with stop-after-current on so the next one does not
    /// start; a seek or skip that leaves more to play stretches the wait.
    /// Playback then ends, faded with `fade_out`, and close, sleep and shutdown
    /// get their [`FINAL_COUNTDOWN`].
    pub fn tick(&self, now: Instant, player: &dyn Playback) -> Tick {
        let _calls = self.lock_calls();
        std::mem::take(&mut *self.lock_undo()).apply(player);
        let Some(armed) = self.lock().clone() else {
            return Tick::default();
        };
        if let Some(level) = armed.fade_level(now) {
            self.fade(&armed, level, player);
            return Tick::default();
        }
        if armed.fires_at > now {
            return Tick::default();
        }
        match armed.phase {
            Phase::Timer if armed.options.complete_last_track => {
                match player.playing().filter(|p| !p.track_left.is_zero()) {
                    Some(playing) => self.wait_for_track(&armed, playing, now, player),
                    None => self.end_playback(&armed, now, player),
                }
            }
            Phase::LastTrack => match player.playing().filter(|p| p.track_left > TRACK_END_SLACK) {
                Some(playing) => Tick {
                    fired: None,
                    changed: self.update(armed.id, |a| a.fires_at = now + playing.track_left),
                },
                None => self.end_playback(&armed, now, player),
            },
            Phase::Timer => self.end_playback(&armed, now, player),
            Phase::Warning => Tick {
                fired: self.take(armed.id).then_some(armed.action),
                changed: true,
            },
        }
    }

    /// Puts the volume back as the server stops, so MusicBee never keeps a faded
    /// one. A timer still set fades again from the volume it finds once the
    /// server is back; stop-after-current stays on for a wait still running.
    pub fn hand_back(&self, player: &dyn Playback) {
        let _calls = self.lock_calls();
        let mut undo = std::mem::take(&mut *self.lock_undo());
        if let Some(armed) = self.lock().as_mut() {
            undo.volume = undo.volume.or(armed.undo.volume.take());
        }
        undo.apply(player);
    }

    fn fade(&self, armed: &Armed, level: f64, player: &dyn Playback) {
        let from = armed.undo.volume.or_else(|| {
            let volume = player.playing_volume()?;
            self.update(armed.id, |a| a.undo.volume = Some(volume))
                .then_some(volume)
        });
        if let Some(from) = from {
            player.set_volume(faded(from, level));
        }
    }

    fn wait_for_track(
        &self,
        armed: &Armed,
        playing: Playing,
        now: Instant,
        player: &dyn Playback,
    ) -> Tick {
        let turn_on = !playing.stop_after_current;
        if turn_on {
            player.set_stop_after_current(true);
        }
        let changed = self.update(armed.id, |a| {
            a.phase = Phase::LastTrack;
            a.started = now;
            a.fires_at = now + playing.track_left;
            a.undo.stop_after_current = turn_on;
        });
        if !changed && turn_on {
            player.set_stop_after_current(false);
        }
        Tick {
            fired: None,
            changed,
        }
    }

    /// Ends playback for a timer that faded or waited out the track, then fires
    /// `stop` or starts the warning.
    ///
    /// The timer moves on before any player call, so one cancelled or replaced
    /// meanwhile is left alone. Playback is stopped before stop-after-current
    /// goes off, so a track end that MusicBee reaches later cannot start the next.
    fn end_playback(&self, armed: &Armed, now: Instant, player: &dyn Playback) -> Tick {
        let fires = !armed.action.needs_permission();
        let moved_on = if fires {
            self.take(armed.id)
        } else {
            self.update(armed.id, |a| {
                a.phase = Phase::Warning;
                a.started = now;
                a.fires_at = now + FINAL_COUNTDOWN;
                a.undo = Undo::default();
            })
        };
        if !moved_on {
            return Tick::default();
        }
        if armed.undo != Undo::default() {
            player.stop();
        }
        armed.undo.apply(player);
        Tick {
            fired: fires.then_some(armed.action),
            changed: true,
        }
    }

    /// Changes the timer if it is still the one with `id`.
    fn update(&self, id: u64, change: impl FnOnce(&mut Armed)) -> bool {
        let mut armed = self.lock();
        match armed.as_mut().filter(|a| a.id == id) {
            Some(a) => {
                change(a);
                true
            }
            None => false,
        }
    }

    /// Removes the timer if it is still the one with `id`.
    fn take(&self, id: u64) -> bool {
        let mut armed = self.lock();
        let current = armed.as_ref().is_some_and(|a| a.id == id);
        if current {
            *armed = None;
        }
        current
    }

    /// Waits for the next set or cancel.
    pub async fn changed(&self) {
        self.changed.notified().await;
    }

    fn lock(&self) -> MutexGuard<'_, Option<Armed>> {
        self.armed.lock().unwrap_or_else(|e| e.into_inner())
    }

    fn lock_calls(&self) -> MutexGuard<'_, ()> {
        self.player_calls.lock().unwrap_or_else(|e| e.into_inner())
    }

    fn lock_undo(&self) -> MutexGuard<'_, Undo> {
        self.undo.lock().unwrap_or_else(|e| e.into_inner())
    }
}

/// `volume` scaled to `level`, a share from 0 to 1.
fn faded(volume: i32, level: f64) -> i32 {
    (f64::from(volume) * level.clamp(0.0, 1.0)).round() as i32
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

/// Whether `principal` is a paired client, the only kind that can be allowed.
fn can_hold(principal: &Principal) -> bool {
    matches!(principal, Principal::Paired(_))
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

    /// Takes the permission from every paired client, as unpairing them all does.
    pub fn revoke_paired(&self) {
        for (key, _) in self.grants.granted() {
            if let Some(paired @ Principal::Paired(_)) = Principal::from_key(&key) {
                self.revoke(&paired);
            }
        }
    }

    /// Answers a power op for `principal`; `None` for any other op.
    ///
    /// `may_stop` is asked only when the request stops playback or cancels a
    /// `stop` timer: Party Mode's verdict on the Playback capability.
    pub fn handle(
        &self,
        principal: &Principal,
        op: &str,
        data: &Value,
        may_stop: &dyn Fn() -> Result<(), String>,
    ) -> Option<Result<Value, V6Error>> {
        let answer = match op {
            "system_auto_shutdown" => Ok(self.timer.status(Instant::now())),
            "system_set_auto_shutdown" => self.set(principal, data, may_stop, Instant::now()),
            _ => return None,
        };
        Some(answer.map(|status| {
            let mut out = status.to_json();
            out["power_allowed"] = json!(self.grants.allows(principal));
            out
        }))
    }

    fn set(
        &self,
        principal: &Principal,
        data: &Value,
        may_stop: &dyn Fn() -> Result<(), String>,
        now: Instant,
    ) -> Result<Status, V6Error> {
        let name = req_str(data, "action")?;
        let allowed = self.grants.allows(principal);
        let refused = |message: String| V6Error::new(ErrorCode::Forbidden, message);
        if name == "cancel" {
            if self.timer.status(now).action == Some(PowerAction::Stop) {
                may_stop().map_err(refused)?;
            }
            return self
                .timer
                .replace(None, allowed, now)
                .ok_or_else(|| refused(GUARDED.into()));
        }
        let action = PowerAction::parse(name).ok_or_else(|| {
            V6Error::field(
                ErrorCode::InvalidField,
                "action",
                "action must be stop, close, sleep, shutdown or cancel",
            )
        })?;
        if action.needs_permission() && !allowed {
            return Err(refused(NOT_ALLOWED.into()));
        }
        if action == PowerAction::Stop {
            may_stop().map_err(refused)?;
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
        let next = Plan {
            action,
            after: Duration::from_secs(minutes * 60),
            by: principal.key(),
            options: Options {
                fade_out: opt_bool(data, "fade_out")?.unwrap_or(false),
                complete_last_track: opt_bool(data, "complete_last_track")?.unwrap_or(false),
            },
        };
        self.timer
            .replace(Some(next), allowed, now)
            .ok_or_else(|| refused(GUARDED.into()))
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
        Principal::Paired(id.into())
    }

    fn anyone_may_stop() -> Result<(), String> {
        Ok(())
    }

    fn set(power: &Power, who: &Principal, action: &str, minutes: u64) -> Result<Value, V6Error> {
        power
            .handle(
                who,
                "system_set_auto_shutdown",
                &json!({ "action": action, "minutes": minutes }),
                &anyone_may_stop,
            )
            .unwrap()
    }

    fn cancel(power: &Power, who: &Principal) -> Result<Value, V6Error> {
        power
            .handle(
                who,
                "system_set_auto_shutdown",
                &json!({ "action": "cancel" }),
                &anyone_may_stop,
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
    fn a_device_that_may_stop_playback_may_set_or_cancel_a_stop_timer() {
        let power = Power::default();
        assert_eq!(
            set(&power, &Principal::Anonymous, "stop", 30).unwrap()["action"],
            "stop"
        );
        let out = cancel(&power, &Principal::Anonymous).unwrap();
        assert_eq!(out["active"], false);
        assert_eq!(out["power_allowed"], false);
    }

    #[test]
    fn stopping_or_cancelling_a_stop_timer_asks_party_mode() {
        let power = Power::default();
        let refuse = || Err("guests may not stop playback".to_owned());
        let stop = json!({ "action": "stop", "minutes": 5 });
        let ask = |data: &Value| {
            power
                .handle(&app("guest"), "system_set_auto_shutdown", data, &refuse)
                .unwrap()
        };
        assert_eq!(ask(&stop).unwrap_err().code, ErrorCode::Forbidden);

        set(&power, &app("host"), "stop", 5).unwrap();
        let err = ask(&json!({ "action": "cancel" })).unwrap_err();
        assert_eq!(err.message, "guests may not stop playback");
        assert!(power.timer.deadline().is_some());
    }

    #[test]
    fn only_an_allowed_device_may_cancel_or_replace_a_shutdown() {
        let power = Power::default();
        power.grants.grant(&app("phone"), 1);
        set(&power, &app("phone"), "shutdown", 10).unwrap();

        let guest = app("guest");
        assert_eq!(
            cancel(&power, &guest).unwrap_err().code,
            ErrorCode::Forbidden
        );
        assert_eq!(
            set(&power, &guest, "stop", 10).unwrap_err().code,
            ErrorCode::Forbidden,
            "a stop timer would replace the shutdown"
        );
        assert_eq!(
            power.timer.status(Instant::now()).action,
            Some(PowerAction::Shutdown)
        );

        power.grants.grant(&app("tablet"), 1);
        assert_eq!(cancel(&power, &app("tablet")).unwrap()["active"], false);
    }

    #[test]
    fn only_a_proven_identity_can_be_allowed() {
        let grants = PowerGrants::default();
        assert!(!grants.grant(&Principal::Anonymous, 1));
        assert!(!grants.grant(&Principal::LegacyDevice("android-1.6".into()), 1));
        assert!(grants.grant(&Principal::Paired("desk".into()), 1));
        assert!(grants.allows(&Principal::Paired("desk".into())));
        assert!(
            !grants.allows(&Principal::Paired("laptop".into())),
            "a grant is one device's"
        );
    }

    const SECOND: Duration = Duration::from_secs(1);

    /// A player that records each call and plays what it is given.
    #[derive(Default)]
    struct FakePlayer {
        playing: Mutex<Option<Playing>>,
        calls: Mutex<Vec<String>>,
    }

    impl FakePlayer {
        fn playing(volume: i32, track_left: Duration) -> Self {
            let player = Self::default();
            *player.playing.lock().unwrap() = Some(Playing {
                volume,
                track_left,
                stop_after_current: false,
            });
            player
        }

        fn with_stop_after_current(self) -> Self {
            if let Some(playing) = self.playing.lock().unwrap().as_mut() {
                playing.stop_after_current = true;
            }
            self
        }

        /// MusicBee reaching the end of the track with stop-after-current on.
        fn track_ends(&self) {
            *self.playing.lock().unwrap() = None;
        }

        fn calls(&self) -> Vec<String> {
            std::mem::take(&mut *self.calls.lock().unwrap())
        }

        fn record(&self, call: String) {
            self.calls.lock().unwrap().push(call);
        }
    }

    impl Playback for FakePlayer {
        fn playing(&self) -> Option<Playing> {
            *self.playing.lock().unwrap()
        }

        fn playing_volume(&self) -> Option<i32> {
            Playback::playing(self).map(|p| p.volume)
        }

        fn set_volume(&self, volume: i32) {
            self.record(format!("volume {volume}"));
        }

        fn stop(&self) {
            *self.playing.lock().unwrap() = None;
            self.record("stop".into());
        }

        fn set_stop_after_current(&self, on: bool) {
            self.record(format!("stop_after_current {on}"));
        }
    }

    fn plan(action: PowerAction, after: Duration, fade_out: bool, complete: bool) -> Plan {
        Plan {
            action,
            after,
            by: None,
            options: Options {
                fade_out,
                complete_last_track: complete,
            },
        }
    }

    fn fired(action: PowerAction) -> Tick {
        Tick {
            fired: Some(action),
            changed: true,
        }
    }

    const MOVED_ON: Tick = Tick {
        fired: None,
        changed: true,
    };

    #[test]
    fn a_timer_on_the_list_gets_a_final_countdown_and_stop_does_not() {
        let (timer, idle) = (AutoShutdown::default(), FakePlayer::default());
        let start = Instant::now();
        timer.set(plan(PowerAction::Shutdown, MINUTE, false, false), start);
        assert_eq!(timer.tick(start + MINUTE / 2, &idle), Tick::default());
        assert_eq!(timer.tick(start + MINUTE, &idle), MOVED_ON, "the warning");
        let warning = timer.status(start + MINUTE);
        assert!(warning.final_countdown);
        assert_eq!(warning.remaining, FINAL_COUNTDOWN);
        assert_eq!(
            timer.tick(start + MINUTE + FINAL_COUNTDOWN, &idle),
            fired(PowerAction::Shutdown)
        );
        assert_eq!(timer.status(start).action, None);

        timer.set(plan(PowerAction::Stop, MINUTE, false, false), start);
        assert_eq!(timer.tick(start + MINUTE, &idle), fired(PowerAction::Stop));
        assert!(idle.calls().is_empty(), "no option, no player calls");
    }

    #[test]
    fn a_cancel_during_the_final_countdown_keeps_the_pc_on() {
        let (timer, idle) = (AutoShutdown::default(), FakePlayer::default());
        let start = Instant::now();
        timer.set(plan(PowerAction::Sleep, MINUTE, false, false), start);
        timer.tick(start + MINUTE, &idle);
        timer.cancel(start + MINUTE);
        assert_eq!(
            timer.tick(start + MINUTE + FINAL_COUNTDOWN, &idle),
            Tick::default()
        );
    }

    #[test]
    fn fade_out_lowers_the_volume_over_the_last_thirty_seconds() {
        let (timer, player) = (AutoShutdown::default(), FakePlayer::playing(80, MINUTE));
        let start = Instant::now();
        timer.set(plan(PowerAction::Stop, MINUTE, true, false), start);
        let fade = start + MINUTE - FADE_OUT;
        assert_eq!(timer.next_wake(start), Some(fade));
        timer.tick(fade - SECOND, &player);
        assert!(player.calls().is_empty(), "too early to fade");

        timer.tick(fade, &player);
        timer.tick(fade + 15 * SECOND, &player);
        assert_eq!(player.calls(), ["volume 80", "volume 40"]);
        assert_eq!(
            timer.next_wake(fade + 15 * SECOND),
            Some(fade + 15 * SECOND + FADE_STEP)
        );
    }

    #[test]
    fn fade_out_stops_playback_then_puts_the_volume_back() {
        let (timer, player) = (AutoShutdown::default(), FakePlayer::playing(80, MINUTE));
        let start = Instant::now();
        timer.set(plan(PowerAction::Sleep, MINUTE, true, false), start);
        timer.tick(start + 45 * SECOND, &player);
        player.calls();

        assert_eq!(timer.tick(start + MINUTE, &player), MOVED_ON);
        assert_eq!(player.calls(), ["stop", "volume 80"]);
        assert!(timer.status(start + MINUTE).final_countdown);
        timer.tick(start + MINUTE + SECOND, &player);
        assert!(player.calls().is_empty(), "the warning does not fade");
    }

    #[test]
    fn cancelling_or_replacing_a_fade_puts_the_volume_back() {
        let player = FakePlayer::playing(80, MINUTE);
        let start = Instant::now();
        let midway = start + 45 * SECOND;
        let undo = |change: &dyn Fn(&AutoShutdown)| {
            let timer = AutoShutdown::default();
            timer.set(plan(PowerAction::Stop, MINUTE, true, false), start);
            timer.tick(midway, &player);
            change(&timer);
            player.calls();
            timer.tick(midway, &player);
            player.calls()
        };
        assert_eq!(
            undo(&|t| {
                t.cancel(midway);
            }),
            ["volume 80"]
        );
        let replace = plan(PowerAction::Stop, 10 * MINUTE, false, false);
        assert_eq!(
            undo(&|t| {
                t.set(replace.clone(), midway);
            }),
            ["volume 80"]
        );
    }

    #[test]
    fn complete_last_track_waits_for_the_playing_track() {
        let (timer, player) = (
            AutoShutdown::default(),
            FakePlayer::playing(80, 90 * SECOND),
        );
        let start = Instant::now();
        timer.set(plan(PowerAction::Sleep, MINUTE, false, true), start);

        assert_eq!(timer.tick(start + MINUTE, &player), MOVED_ON);
        let waiting = timer.status(start + MINUTE);
        assert!(waiting.waiting_for_track && !waiting.final_countdown);
        assert_eq!(
            waiting.remaining,
            90 * SECOND,
            "remaining_ms runs to the end of the track"
        );
        assert_eq!(player.calls(), ["stop_after_current true"]);

        let track_end = start + MINUTE + 90 * SECOND;
        player.track_ends();
        assert_eq!(timer.tick(track_end, &player), MOVED_ON);
        assert_eq!(
            player.calls(),
            ["stop", "stop_after_current false"],
            "stopped first, so a late track end cannot start the next"
        );
        assert!(timer.status(track_end).final_countdown);
    }

    #[test]
    fn complete_last_track_with_nothing_playing_goes_straight_on() {
        let (timer, idle) = (AutoShutdown::default(), FakePlayer::default());
        let start = Instant::now();
        timer.set(plan(PowerAction::Stop, MINUTE, true, true), start);
        assert_eq!(timer.tick(start + MINUTE, &idle), fired(PowerAction::Stop));
        assert!(idle.calls().is_empty());
    }

    #[test]
    fn complete_last_track_leaves_a_stop_after_current_the_user_set() {
        let player = FakePlayer::playing(80, 90 * SECOND).with_stop_after_current();
        let timer = AutoShutdown::default();
        let start = Instant::now();
        timer.set(plan(PowerAction::Stop, MINUTE, false, true), start);
        timer.tick(start + MINUTE, &player);
        timer.cancel(start + MINUTE);
        timer.tick(start + MINUTE, &player);
        assert!(player.calls().is_empty());
    }

    #[test]
    fn cancelling_while_waiting_for_the_track_turns_stop_after_current_off() {
        let (timer, player) = (
            AutoShutdown::default(),
            FakePlayer::playing(80, 90 * SECOND),
        );
        let start = Instant::now();
        timer.set(plan(PowerAction::Shutdown, MINUTE, false, true), start);
        timer.tick(start + MINUTE, &player);
        player.calls();
        timer.cancel(start + MINUTE);
        timer.tick(start + MINUTE, &player);
        assert_eq!(player.calls(), ["stop_after_current false"]);
    }

    #[test]
    fn with_both_options_the_fade_ends_with_the_track() {
        let (timer, player) = (
            AutoShutdown::default(),
            FakePlayer::playing(60, 40 * SECOND),
        );
        let start = Instant::now();
        timer.set(plan(PowerAction::Stop, MINUTE, true, true), start);
        timer.tick(start + 45 * SECOND, &player);
        assert!(player.calls().is_empty(), "the minutes do not fade");

        timer.tick(start + MINUTE, &player);
        let track_end = start + MINUTE + 40 * SECOND;
        timer.tick(track_end - FADE_OUT, &player);
        timer.tick(track_end - FADE_OUT / 2, &player);
        assert_eq!(
            player.calls(),
            ["stop_after_current true", "volume 60", "volume 30"]
        );
        player.track_ends();
        assert_eq!(timer.tick(track_end, &player), fired(PowerAction::Stop));
        assert_eq!(
            player.calls(),
            ["stop", "stop_after_current false", "volume 60"]
        );
    }

    #[test]
    fn a_track_shorter_than_the_fade_fades_from_where_the_wait_begins() {
        let (timer, player) = (
            AutoShutdown::default(),
            FakePlayer::playing(50, 10 * SECOND),
        );
        let start = Instant::now();
        timer.set(plan(PowerAction::Stop, MINUTE, true, true), start);
        timer.tick(start + MINUTE, &player);
        timer.tick(start + MINUTE, &player);
        timer.tick(start + MINUTE + 5 * SECOND, &player);
        assert_eq!(
            player.calls(),
            ["stop_after_current true", "volume 50", "volume 25"]
        );
    }

    #[test]
    fn a_stopping_server_hands_the_volume_back_and_the_fade_resumes() {
        let (timer, player) = (AutoShutdown::default(), FakePlayer::playing(80, MINUTE));
        let start = Instant::now();
        timer.set(plan(PowerAction::Stop, MINUTE, true, false), start);
        timer.tick(start + 45 * SECOND, &player);
        player.calls();

        timer.hand_back(&player);
        assert_eq!(player.calls(), ["volume 80"]);
        assert!(timer.deadline().is_some(), "the timer stays set");
        timer.tick(start + 51 * SECOND, &player);
        assert_eq!(player.calls(), ["volume 24"]);
    }

    #[test]
    fn a_seek_back_during_the_wait_stretches_it() {
        let (timer, player) = (
            AutoShutdown::default(),
            FakePlayer::playing(80, 90 * SECOND),
        );
        let start = Instant::now();
        timer.set(plan(PowerAction::Sleep, MINUTE, false, true), start);
        timer.tick(start + MINUTE, &player);

        let first_end = start + MINUTE + 90 * SECOND;
        player.playing.lock().unwrap().as_mut().unwrap().track_left = 20 * SECOND;
        assert_eq!(timer.tick(first_end, &player), MOVED_ON);
        let still = timer.status(first_end);
        assert!(still.waiting_for_track);
        assert_eq!(still.remaining, 20 * SECOND);

        player.playing.lock().unwrap().as_mut().unwrap().track_left = SECOND / 2;
        timer.tick(first_end + 20 * SECOND, &player);
        assert!(
            timer.status(first_end + 20 * SECOND).final_countdown,
            "half a second left is the end"
        );
    }

    #[test]
    fn host_playback_reads_the_time_left_and_only_while_playing() {
        use crate::protocol::messages::{PlaybackPositionResponse, PlayerState};
        let mut host = crate::providers::MockProviders {
            player_state: PlayerState {
                play_state: PlayState::Playing,
                volume: 70,
                stop_after_current: true,
                ..PlayerState::default()
            },
            position: PlaybackPositionResponse {
                current: 1_000,
                total: 61_000,
            },
            ..Default::default()
        };
        let playing = HostPlayback(&host).playing().unwrap();
        assert_eq!(playing.track_left, MINUTE);
        assert_eq!((playing.volume, playing.stop_after_current), (70, true));
        assert_eq!(HostPlayback(&host).playing_volume(), Some(70));

        host.position.total = -1;
        assert_eq!(
            HostPlayback(&host).playing().unwrap().track_left,
            Duration::ZERO,
            "a stream with no length has nothing to wait for"
        );
        host.player_state.play_state = PlayState::Paused;
        assert_eq!(HostPlayback(&host).playing(), None);
        assert_eq!(HostPlayback(&host).playing_volume(), None);
    }

    #[test]
    fn a_stopping_server_leaves_stop_after_current_on_for_the_wait() {
        let (timer, player) = (
            AutoShutdown::default(),
            FakePlayer::playing(80, 90 * SECOND),
        );
        let start = Instant::now();
        timer.set(plan(PowerAction::Sleep, MINUTE, false, true), start);
        timer.tick(start + MINUTE, &player);
        player.calls();

        timer.hand_back(&player);
        assert!(player.calls().is_empty());
        timer.cancel(start + MINUTE);
        timer.tick(start + MINUTE, &player);
        assert_eq!(player.calls(), ["stop_after_current false"]);
    }

    #[test]
    fn the_options_are_read_echoed_and_checked() {
        let power = Power::default();
        let ask = |data: Value| {
            power
                .handle(
                    &Principal::Anonymous,
                    "system_set_auto_shutdown",
                    &data,
                    &anyone_may_stop,
                )
                .unwrap()
        };
        let out = ask(json!({ "action": "stop", "minutes": 5, "fade_out": true })).unwrap();
        assert_eq!(out["fade_out"], true);
        assert_eq!(out["complete_last_track"], false);
        assert_eq!(out["waiting_for_track"], false);

        let bad = json!({ "action": "stop", "minutes": 5, "complete_last_track": "yes" });
        assert_eq!(
            ask(bad).unwrap_err().field.as_deref(),
            Some("complete_last_track")
        );
        let out = ask(json!({ "action": "cancel", "fade_out": "ignored" })).unwrap();
        assert_eq!(out["fade_out"], false);
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
    fn unpairing_everything_takes_every_permission_and_timer() {
        let power = Power::default();
        let desk = Principal::Paired("desk".into());
        power.grants.grant(&desk, 1);
        power.grants.grant(&app("phone"), 1);
        set(&power, &desk, "sleep", 10).unwrap();

        power.revoke_paired();
        assert!(!power.grants.allows(&desk));
        assert!(!power.grants.allows(&app("phone")));
        assert_eq!(power.timer.deadline(), None);
    }

    #[test]
    fn minutes_and_action_are_checked() {
        let power = Power::default();
        let bad = |data: Value| {
            power
                .handle(
                    &Principal::Anonymous,
                    "system_set_auto_shutdown",
                    &data,
                    &anyone_may_stop,
                )
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
                .handle(
                    &Principal::Anonymous,
                    "player_stop",
                    &json!({}),
                    &anyone_may_stop
                )
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
