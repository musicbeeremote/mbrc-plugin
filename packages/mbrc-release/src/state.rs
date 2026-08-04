//! Machine-written update bookkeeping: `update_state.json`.
//!
//! This is deliberately *not* in `core_settings.json`. That file is the user's
//! preferences, edited by the Configure panel, and the panel round-trips only the
//! fields it knows about. Putting a skipped version or a cached ETag in there
//! would mean a save of an unrelated setting could silently un-skip a release -
//! a bug whose cause nobody would ever guess from the symptom.
//!
//! It also draws the right line: nothing here is a preference. It is the record
//! of what the updater has already done.

use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};
use time::{format_description::well_known::Rfc3339, Duration, OffsetDateTime};

use crate::error::{Result, UpdateError};

/// Filename under the core's storage directory.
pub const STATE_FILE: &str = "update_state.json";

#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(default)]
pub struct UpdateState {
    /// When the last check completed, RFC3339. Written for a 304 too: the point
    /// is to rate-limit checks, and a 304 was a check.
    pub last_check: Option<String>,
    /// The `ETag` of the release document the last successful check saw.
    pub etag: Option<String>,
    /// A version the user chose to skip. Compared as a string, not as semver: it
    /// records "not this one", and any newer release is a different string.
    pub skipped_version: Option<String>,
    /// Checks that have failed in a row. Drives the retry backoff so a network
    /// that is down does not mean a request on every tick, and so a check that
    /// fails does not have to wait the full interval either.
    pub consecutive_failures: u32,
}

/// First retry delay after a failed check; each further failure doubles it.
const FIRST_RETRY_MINUTES: i64 = 15;

/// Where doubling stops (2^6 x 15 minutes = 16 hours), so the delay stays under
/// a 24 hour interval and the backoff never outlives it.
const MAX_RETRY_DOUBLINGS: u32 = 6;

impl UpdateState {
    /// Reads the state file. A missing, unreadable, or corrupt file yields the
    /// default state rather than an error: the worst it costs is one extra
    /// check and a re-prompt for a skipped version, and neither is worth failing
    /// the caller over. Use [`load_checked`](Self::load_checked) where the caller
    /// wants to log that it happened.
    pub fn load(dir: &str) -> Self {
        Self::load_checked(dir).unwrap_or_default()
    }

    /// [`load`](Self::load), but reporting a corrupt file so the core can log it.
    /// A missing file is not a failure - it is simply the first run.
    pub fn load_checked(dir: &str) -> Result<Self> {
        let path = Self::path(dir);
        let Ok(contents) = std::fs::read_to_string(&path) else {
            return Ok(Self::default());
        };
        serde_json::from_str(&contents)
            .map_err(|e| UpdateError::Parse(format!("{STATE_FILE}: {e}")))
    }

    /// Writes the state file, creating the directory if needed.
    ///
    /// Via a temporary file and a rename, so a crash mid-write cannot leave a
    /// half-written file that the next load discards along with the skipped
    /// version it held.
    pub fn save(&self, dir: &str) -> Result<()> {
        std::fs::create_dir_all(dir).map_err(|e| UpdateError::Io(format!("{dir}: {e}")))?;
        let path = Self::path(dir);
        let temp = path.with_extension("json.tmp");
        let json = serde_json::to_string_pretty(self)
            .map_err(|e| UpdateError::Io(format!("serialize update state: {e}")))?;
        std::fs::write(&temp, json)
            .map_err(|e| UpdateError::Io(format!("{}: {e}", temp.display())))?;
        std::fs::rename(&temp, &path).map_err(|e| UpdateError::Io(format!("{STATE_FILE}: {e}")))
    }

    fn path(dir: &str) -> PathBuf {
        Path::new(dir).join(STATE_FILE)
    }

    /// Whether the interval since [`last_check`](Self::last_check) has elapsed.
    ///
    /// An absent or unparseable timestamp is due: never having checked and having
    /// lost the record of checking are the same situation from here.
    pub fn is_due(&self, now: OffsetDateTime, interval_hours: u64) -> bool {
        let Some(last) = self
            .last_check
            .as_deref()
            .and_then(|raw| OffsetDateTime::parse(raw, &Rfc3339).ok())
        else {
            return true;
        };
        // A last_check in the future (a clock that was wound back, or a roamed
        // profile) would otherwise suppress checks until real time caught up.
        if last > now {
            return true;
        }
        now - last >= self.wait(interval_hours)
    }

    /// How long to wait after the last check before the next one.
    ///
    /// Normally the configured interval. After a failure it is the backoff
    /// instead, which is always the shorter of the two: a failed check should be
    /// retried sooner than a successful one, never later.
    fn wait(&self, interval_hours: u64) -> Duration {
        let interval = Duration::hours(interval_hours as i64);
        if self.consecutive_failures == 0 {
            return interval;
        }
        let doublings = (self.consecutive_failures - 1).min(MAX_RETRY_DOUBLINGS);
        let backoff = Duration::minutes(FIRST_RETRY_MINUTES * (1i64 << doublings));
        backoff.min(interval)
    }

    /// Whether the user asked to skip exactly this version.
    pub fn is_skipped(&self, version: &str) -> bool {
        self.skipped_version.as_deref() == Some(version)
    }

    /// Records that a check happened at `now`, keeping the server's `ETag` when
    /// it sent one and the previous one otherwise (a 304 carries no `ETag`).
    pub fn record_check(&mut self, now: OffsetDateTime, etag: Option<String>) {
        self.last_check = now.format(&Rfc3339).ok();
        if etag.is_some() {
            self.etag = etag;
        }
    }

    /// Records a failed check, so the next one waits out the backoff.
    pub fn record_failure(&mut self, now: OffsetDateTime) {
        self.last_check = now.format(&Rfc3339).ok();
        self.consecutive_failures = self.consecutive_failures.saturating_add(1);
    }

    /// Clears the failure streak after a check that worked.
    pub fn clear_failures(&mut self) {
        self.consecutive_failures = 0;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_dir(name: &str) -> String {
        let dir = std::env::temp_dir().join(name);
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir.to_string_lossy().into_owned()
    }

    fn at(raw: &str) -> OffsetDateTime {
        OffsetDateTime::parse(raw, &Rfc3339).unwrap()
    }

    #[test]
    fn round_trips_through_the_file() {
        let dir = temp_dir("mbrc-update-state-roundtrip");
        let mut state = UpdateState::default();
        state.record_check(at("2026-08-04T10:00:00Z"), Some("\"abc\"".into()));
        state.skipped_version = Some("1.6.0".into());
        state.save(&dir).unwrap();

        let loaded = UpdateState::load(&dir);
        assert_eq!(loaded, state);
        assert!(loaded.is_skipped("1.6.0"));
        assert!(!loaded.is_skipped("1.6.1"));
    }

    #[test]
    fn a_missing_or_corrupt_file_is_the_default_state() {
        let missing = "/no/such/dir/at/all";
        assert_eq!(UpdateState::load(missing), UpdateState::default());
        // A first run is not an error; a corrupt file is one worth logging.
        assert!(UpdateState::load_checked(missing).is_ok());

        let dir = temp_dir("mbrc-update-state-corrupt");
        std::fs::write(Path::new(&dir).join(STATE_FILE), "{ not json").unwrap();
        assert_eq!(UpdateState::load(&dir), UpdateState::default());
        assert!(UpdateState::load_checked(&dir).is_err());
    }

    #[test]
    fn the_interval_gates_checks() {
        let mut state = UpdateState::default();
        assert!(state.is_due(at("2026-08-04T10:00:00Z"), 24)); // never checked

        state.record_check(at("2026-08-04T10:00:00Z"), None);
        assert!(!state.is_due(at("2026-08-04T20:00:00Z"), 24));
        assert!(state.is_due(at("2026-08-05T10:00:00Z"), 24)); // exactly 24h
        assert!(state.is_due(at("2026-08-04T11:00:00Z"), 0)); // interval 0 = always
    }

    #[test]
    fn a_failed_check_is_retried_sooner_than_the_interval() {
        let mut state = UpdateState::default();
        state.record_failure(at("2026-08-04T10:00:00Z"));
        assert_eq!(state.consecutive_failures, 1);

        // 15 minutes after the first failure, not 24 hours.
        assert!(!state.is_due(at("2026-08-04T10:10:00Z"), 24));
        assert!(state.is_due(at("2026-08-04T10:15:00Z"), 24));

        state.record_failure(at("2026-08-04T10:15:00Z"));
        assert!(!state.is_due(at("2026-08-04T10:35:00Z"), 24)); // doubled to 30m
        assert!(state.is_due(at("2026-08-04T10:45:00Z"), 24));

        // The backoff never exceeds the configured interval: an hourly check
        // stays hourly however long the failure streak gets.
        for _ in 0..20 {
            state.record_failure(at("2026-08-04T10:45:00Z"));
        }
        assert!(state.is_due(at("2026-08-04T11:45:00Z"), 1));

        state.clear_failures();
        assert!(!state.is_due(at("2026-08-04T11:00:00Z"), 24));
    }

    #[test]
    fn a_last_check_in_the_future_does_not_suppress_checks() {
        let mut state = UpdateState::default();
        state.record_check(at("2027-01-01T00:00:00Z"), None);
        assert!(state.is_due(at("2026-08-04T10:00:00Z"), 24));
    }

    #[test]
    fn a_304_keeps_the_previous_etag() {
        let mut state = UpdateState::default();
        state.record_check(at("2026-08-04T10:00:00Z"), Some("\"v1\"".into()));
        state.record_check(at("2026-08-05T10:00:00Z"), None);
        assert_eq!(state.etag.as_deref(), Some("\"v1\""));
        assert_eq!(state.last_check.as_deref(), Some("2026-08-05T10:00:00Z"));
    }
}
