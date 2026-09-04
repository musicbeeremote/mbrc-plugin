//! The update flow as the settings panel sees it: one status, one job at a time.
//!
//! Everything the panel can ask for - check, download, skip - is a
//! fire-and-forget host command that starts a background thread, plus a status
//! the panel polls (and a push event so it rarely has to). The panel therefore
//! never blocks MusicBee's UI thread on a network request, and the core never
//! has to be told which release to fetch: the only download it will perform is
//! the one its own verified check produced.
//!
//! Two rules hold the state machine together:
//!
//! - **One job at a time.** A check and a download both talk to github.com and
//!   both write the same status; running two would make the status a lie. A
//!   second request while one runs is refused, not queued.
//! - **The staged bundle wins.** If a version is already staged on disk, that is
//!   what the user is offered, even if a fresh check finds the same version
//!   "available" - the download already happened.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, MutexGuard};

use mbrc_release::{check::CheckOutcome, AvailableUpdate, HttpClient, Result, UpdateState};

use crate::ffi::types::{HostEventType, MbrcResult};
use crate::state::Core;

/// No check has run yet this session and nothing is staged.
pub const STATE_UNKNOWN: &str = "unknown";
/// A check is in flight.
pub const STATE_CHECKING: &str = "checking";
/// The published release is not newer than what is running.
pub const STATE_UP_TO_DATE: &str = "up_to_date";
/// A newer release is available and has not been downloaded.
pub const STATE_AVAILABLE: &str = "available";
/// The available release is being downloaded and staged.
pub const STATE_DOWNLOADING: &str = "downloading";
/// A verified bundle is staged and waiting for a restart.
pub const STATE_STAGED: &str = "staged";
/// A newer release exists but the user asked not to be offered this one.
pub const STATE_SKIPPED: &str = "skipped";
/// Automatic checking is off and the caller did not force a check.
pub const STATE_DISABLED: &str = "disabled";
/// The last check failed; `message` says how, and so does the log.
pub const STATE_ERROR: &str = "error";
/// The download of an available update failed.
///
/// Distinct from [`STATE_ERROR`] because the update is still known and still
/// worth retrying - and because "no update could be found" is the wrong thing
/// to tell someone who just pressed Download.
pub const STATE_DOWNLOAD_FAILED: &str = "download_failed";

/// The status is a generated DTO (the C# side reads it by field name), so it
/// lives with the rest of them; the states it can be in are decided here.
pub use crate::ffi::dtos::UpdateStatus;

impl Default for UpdateStatus {
    fn default() -> Self {
        Self {
            state: STATE_UNKNOWN.to_owned(),
            version: String::new(),
            notes_url: String::new(),
            min_musicbee_build: 0,
            message: String::new(),
            checked_at: String::new(),
        }
    }
}

impl UpdateStatus {
    fn with_state(state: &str) -> Self {
        Self {
            state: state.to_owned(),
            ..Self::default()
        }
    }
}

/// The current status, `None` until the first read seeds it from disk.
static STATUS: Mutex<Option<UpdateStatus>> = Mutex::new(None);
/// The verified update the last check produced, kept so a download has something
/// to fetch that the host did not name.
static AVAILABLE: Mutex<Option<Box<AvailableUpdate>>> = Mutex::new(None);
/// Set while a check or a download is running.
static BUSY: AtomicBool = AtomicBool::new(false);

fn status_lock() -> MutexGuard<'static, Option<UpdateStatus>> {
    STATUS
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner())
}

fn available_lock() -> MutexGuard<'static, Option<Box<AvailableUpdate>>> {
    AVAILABLE
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner())
}

/// Claims the right to run a job, releasing it on drop - including on a panic,
/// so a job that dies does not wedge the panel's buttons for the session.
struct Job;

impl Job {
    fn claim() -> Option<Self> {
        if BUSY.swap(true, Ordering::AcqRel) {
            None
        } else {
            Some(Self)
        }
    }
}

impl Drop for Job {
    fn drop(&mut self) {
        BUSY.store(false, Ordering::Release);
    }
}

/// The status, seeded from disk on first read: a bundle staged in an earlier
/// session is the answer even before anything has run this one.
pub fn status(core: &Core) -> UpdateStatus {
    let mut guard = status_lock();
    guard
        .get_or_insert_with(|| initial_status(&core.config.storage_path))
        .clone()
}

/// Serializes the status as MessagePack for the settings panel.
pub fn status_bytes(core: &Core) -> Option<Vec<u8>> {
    rmp_serde::to_vec_named(&status(core)).ok()
}

/// Starts a background check. `force` bypasses both the `update_check_enabled`
/// preference and the interval; the panel's button forces, the startup check
/// does not.
pub fn start_check(core: Arc<Core>, force: bool) -> MbrcResult {
    let Some(job) = Job::claim() else {
        return MbrcResult::AlreadyRunning;
    };
    // What the panel is showing now, kept so an outcome that turns out to be no
    // news can put it back.
    let previous = status(&core);
    // Only a forced check announces itself: the startup one has nobody waiting,
    // and would flash "Checking..." over an offer the user is reading.
    if force {
        publish(&core, UpdateStatus::with_state(STATE_CHECKING));
    }
    spawn_publishing(core, job, move |core| match client(core) {
        Ok(client) => run_check(core, client.as_ref(), force, previous),
        Err(e) => failure("could not start the update check", &e),
    });
    MbrcResult::Ok
}

/// Runs `work` on a background thread and publishes whatever status it returns.
///
/// The job guard is held for the thread's life. The core can be shut down
/// between the request and this thread being scheduled, and there is then no
/// host to report to, so it returns without publishing.
fn spawn_publishing(
    core: Arc<Core>,
    job: Job,
    work: impl FnOnce(&Core) -> UpdateStatus + Send + 'static,
) {
    std::thread::spawn(move || {
        let _job = job;
        if !crate::state::is_initialized() {
            return;
        }
        let status = work(&core);
        publish(&core, status);
    });
}

/// Starts a background download of the update the last check produced. Refused
/// when there is none: the host cannot name a release, only accept the one the
/// core verified.
pub fn start_download(core: Arc<Core>) -> MbrcResult {
    let Some(update) = available_lock().clone() else {
        tracing::warn!("a download was requested with no verified update to fetch");
        return MbrcResult::InvalidArgument;
    };
    let Some(job) = Job::claim() else {
        return MbrcResult::AlreadyRunning;
    };
    publish(&core, downloading(&update));
    spawn_publishing(core, job, move |core| match client(core) {
        Ok(client) => run_download(core, client.as_ref(), &update),
        Err(e) => download_failure(&update, &e),
    });
    MbrcResult::Ok
}

/// Records that the user does not want to be offered the available version again.
///
/// Only an actual offer can be skipped. `version` is populated for `up_to_date`
/// too (it names the latest published release), so skipping "whatever the status
/// names" would let a click that lands just after a status change permanently
/// suppress the release the user is already running - and there is no UI to
/// undo that.
pub fn skip_available(core: &Core) -> MbrcResult {
    let current = status(core);
    let offered = matches!(
        current.state.as_str(),
        STATE_AVAILABLE | STATE_DOWNLOAD_FAILED
    );
    let version = current.version;
    if !offered || version.is_empty() {
        tracing::warn!(state = %current.state, "nothing on offer to skip");
        return MbrcResult::InvalidArgument;
    }
    if let Err(e) = super::skip_version(&core.config, &version) {
        tracing::warn!(error = %e, version = %version, "could not record the skipped version");
        return MbrcResult::RuntimeError;
    }
    tracing::info!(version = %version, "the user skipped a release");
    available_lock().take();
    publish(
        core,
        UpdateStatus {
            version,
            ..UpdateStatus::with_state(STATE_SKIPPED)
        },
    );
    MbrcResult::Ok
}

/// Runs the check and turns its result into the new status, stashing the
/// verified update so a later download has something to fetch.
fn run_check(
    core: &Core,
    client: &dyn HttpClient,
    force: bool,
    previous: UpdateStatus,
) -> UpdateStatus {
    // The plugin's version: the release is its, and the host knows what it runs.
    // Both come from the same `Directory.Build.props`, so the fallback is safe.
    let current = core.providers.plugin_version().unwrap_or_else(|e| {
        tracing::warn!(error = %e, "the host could not report its version; using the core's");
        super::CORE_VERSION.to_owned()
    });

    let outcome = super::check_for_update(client, &core.config, &current, force);
    let checked_at = UpdateState::load(&core.config.storage_path)
        .last_check
        .unwrap_or_default();
    let staged = staged_version(&core.config.storage_path);

    match interpret(outcome, staged.as_deref(), &checked_at) {
        Verdict::Fresh(status, available) => {
            *available_lock() = available;
            status
        }
        // Nothing learned, so nothing is disturbed - the stashed update in
        // particular, which is what the Download button acts on.
        Verdict::NoNews => no_news(previous, staged.as_deref(), &checked_at),
    }
}

/// What a check result means for the two things a check can change: the status,
/// and the verified update kept for a later download.
enum Verdict {
    /// A new answer. This status, and this (possibly absent) update to stash.
    Fresh(UpdateStatus, Option<Box<AvailableUpdate>>),
    /// No new information: a `304`, or a check that was not due. Whatever was
    /// already known still holds, offers included.
    NoNews,
}

/// The status when a check told us nothing new.
///
/// An offer the user has not acted on outlives it - dropping back to "up to
/// date" would take the Download button away from someone who was about to press
/// it, and the second check of a session is a `304` almost every time. Anything
/// else falls back to what the disk says.
fn no_news(previous: UpdateStatus, staged: Option<&str>, checked_at: &str) -> UpdateStatus {
    let actionable = matches!(
        previous.state.as_str(),
        STATE_AVAILABLE | STATE_DOWNLOAD_FAILED | STATE_STAGED | STATE_SKIPPED
    );
    let mut status = if actionable { previous } else { quiet(staged) };
    status.checked_at = checked_at.to_owned();
    status
}

/// Maps a check result onto the status and, when there is one to keep, the
/// verified update behind it.
///
/// Split out from the network call so the mapping - which is where the states
/// the panel switches on are decided - is testable without one.
fn interpret(outcome: Result<CheckOutcome>, staged: Option<&str>, checked_at: &str) -> Verdict {
    let stamp = |mut status: UpdateStatus| {
        status.checked_at = checked_at.to_owned();
        status
    };

    match outcome {
        Ok(CheckOutcome::Available(update)) => {
            // Already downloaded in an earlier session: offer the restart, not
            // the download.
            let state = if staged == Some(update.manifest.version.as_str()) {
                STATE_STAGED
            } else {
                STATE_AVAILABLE
            };
            let status = stamp(UpdateStatus {
                version: update.manifest.version.clone(),
                notes_url: update.manifest.notes_url.clone(),
                min_musicbee_build: update.manifest.min_musicbee_build,
                ..UpdateStatus::with_state(state)
            });
            Verdict::Fresh(status, Some(update))
        }
        Ok(CheckOutcome::UpToDate { latest }) => Verdict::Fresh(
            stamp(UpdateStatus {
                version: latest,
                ..UpdateStatus::with_state(STATE_UP_TO_DATE)
            }),
            None,
        ),
        Ok(CheckOutcome::Skipped { version }) => Verdict::Fresh(
            stamp(UpdateStatus {
                version,
                ..UpdateStatus::with_state(STATE_SKIPPED)
            }),
            None,
        ),
        // Unchanged since our ETag, or never asked: neither may retract an
        // offer already on screen.
        Ok(CheckOutcome::NotModified) | Ok(CheckOutcome::NotDue) => Verdict::NoNews,
        Ok(CheckOutcome::Disabled) => {
            Verdict::Fresh(UpdateStatus::with_state(STATE_DISABLED), None)
        }
        Err(e) => {
            let detail = e.to_string();
            tracing::warn!(error = %detail, "the update check failed");
            // A staged bundle outlives a failed check, so the restart is still
            // the offer and the failure is a footnote.
            let mut status = match staged {
                Some(_) => quiet(staged),
                None => UpdateStatus::with_state(STATE_ERROR),
            };
            status.message = detail;
            Verdict::Fresh(stamp(status), None)
        }
    }
}

/// The status a check with nothing to report falls back to: the staged bundle if
/// there is one, otherwise up to date.
fn quiet(staged: Option<&str>) -> UpdateStatus {
    match staged {
        Some(version) => UpdateStatus {
            version: version.to_owned(),
            ..UpdateStatus::with_state(STATE_STAGED)
        },
        None => UpdateStatus::with_state(STATE_UP_TO_DATE),
    }
}

/// Downloads and stages the update, leaving the status on the restart the helper
/// is now waiting for.
fn run_download(core: &Core, client: &dyn HttpClient, update: &AvailableUpdate) -> UpdateStatus {
    match super::stage_update(client, &core.config, update) {
        Ok(staged) => UpdateStatus {
            version: staged.version,
            notes_url: update.manifest.notes_url.clone(),
            min_musicbee_build: update.manifest.min_musicbee_build,
            ..UpdateStatus::with_state(STATE_STAGED)
        },
        Err(e) => download_failure(update, &e.to_string()),
    }
}

/// A failed download, which keeps naming the update it was for: the verified
/// update is still stashed, so retrying is one press away.
fn download_failure(update: &AvailableUpdate, detail: &str) -> UpdateStatus {
    tracing::warn!(error = detail, version = %update.manifest.version, "the download failed");
    UpdateStatus {
        version: update.manifest.version.clone(),
        notes_url: update.manifest.notes_url.clone(),
        min_musicbee_build: update.manifest.min_musicbee_build,
        message: detail.to_owned(),
        ..UpdateStatus::with_state(STATE_DOWNLOAD_FAILED)
    }
}

fn downloading(update: &AvailableUpdate) -> UpdateStatus {
    UpdateStatus {
        version: update.manifest.version.clone(),
        notes_url: update.manifest.notes_url.clone(),
        min_musicbee_build: update.manifest.min_musicbee_build,
        ..UpdateStatus::with_state(STATE_DOWNLOADING)
    }
}

/// An error status, logged as it is built: the panel gets one line, the log gets
/// the whole thing.
fn failure(context: &str, detail: &str) -> UpdateStatus {
    tracing::warn!(error = detail, "{context}");
    UpdateStatus {
        message: detail.to_owned(),
        ..UpdateStatus::with_state(STATE_ERROR)
    }
}

/// The status a session starts from: whatever is staged on disk, or nothing.
fn initial_status(storage: &str) -> UpdateStatus {
    match staged_version(storage) {
        Some(version) => UpdateStatus {
            version,
            ..UpdateStatus::with_state(STATE_STAGED)
        },
        None => UpdateStatus::default(),
    }
}

/// The version of the bundle staged for the next restart, if there is one.
fn staged_version(storage: &str) -> Option<String> {
    match mbrc_release::read_pending(storage) {
        Ok(pending) => pending.map(|p| p.version),
        Err(e) => {
            tracing::warn!(error = %e, "the staged-update marker is unreadable");
            None
        }
    }
}

/// Stores the new status and tells the host, so an open panel refreshes without
/// waiting for its poll.
///
/// The event is skipped once the core has been shut down: these jobs run on
/// detached threads that can outlive MusicBee's exit by as long as an HTTP
/// request takes, and `on_event` is a C# delegate that does not survive it.
fn publish(core: &Core, status: UpdateStatus) {
    tracing::debug!(state = %status.state, version = %status.version, "update status");
    let alive = crate::state::is_initialized();
    *status_lock() = Some(status);
    if alive {
        core.providers
            .emit_event(HostEventType::UpdateStatusChanged, &[]);
    }
}

/// The production HTTP client, boxed so the callers above are written once
/// against the trait.
#[cfg(windows)]
fn client(core: &Core) -> std::result::Result<Box<dyn HttpClient>, String> {
    super::http_client(&core.config)
        .map(|c| Box::new(c) as Box<dyn HttpClient>)
        .map_err(|e| e.to_string())
}

/// There is no MusicBee here to update. Kept so the module compiles and its
/// tests run on the Linux CI runner.
#[cfg(not(windows))]
fn client(_core: &Core) -> std::result::Result<Box<dyn HttpClient>, String> {
    Err("updates are only supported on Windows".to_owned())
}

#[cfg(test)]
mod tests {
    use super::*;
    use mbrc_release::{
        manifest::{Artifact, Artifacts},
        Channel, Manifest, UpdateError,
    };

    fn manifest(version: &str) -> Manifest {
        Manifest {
            schema: 1,
            channel: Channel::Stable,
            version: version.to_owned(),
            released_at: "2026-08-16T00:00:00Z".to_owned(),
            abi_version: 1,
            min_musicbee_build: 6500,
            notes_url: "https://example.invalid/notes".to_owned(),
            artifacts: Artifacts {
                zip: Artifact {
                    name: "mbrc.zip".to_owned(),
                    size: 1,
                    sha512: "0".repeat(128),
                },
                installer: Artifact {
                    name: "mbrc.exe".to_owned(),
                    size: 1,
                    sha512: "0".repeat(128),
                },
            },
            files: Vec::new(),
        }
    }

    fn available(version: &str) -> Result<CheckOutcome> {
        Ok(CheckOutcome::Available(Box::new(AvailableUpdate {
            manifest: manifest(version),
            manifest_bytes: Vec::new(),
            signature: String::new(),
            zip_url: "https://example.invalid/mbrc.zip".to_owned(),
            key_name: "test",
        })))
    }

    /// The `Fresh` half of a verdict, for the cases that must produce one.
    fn fresh(verdict: Verdict) -> (UpdateStatus, Option<Box<AvailableUpdate>>) {
        match verdict {
            Verdict::Fresh(status, update) => (status, update),
            Verdict::NoNews => panic!("expected a fresh answer, got NoNews"),
        }
    }

    #[test]
    fn an_available_update_carries_what_the_panel_renders() {
        let (status, update) = fresh(interpret(available("1.6.0"), None, "2026-08-16T10:00:00Z"));
        assert_eq!(status.state, STATE_AVAILABLE);
        assert_eq!(status.version, "1.6.0");
        assert_eq!(status.notes_url, "https://example.invalid/notes");
        assert_eq!(status.min_musicbee_build, 6500);
        assert_eq!(status.checked_at, "2026-08-16T10:00:00Z");
        assert!(status.message.is_empty());
        // Kept, because a download has nothing else to go on.
        assert!(update.is_some());
    }

    #[test]
    fn an_already_staged_version_is_offered_as_a_restart_not_a_download() {
        let (status, _) = fresh(interpret(available("1.6.0"), Some("1.6.0"), ""));
        assert_eq!(status.state, STATE_STAGED);
        assert_eq!(status.version, "1.6.0");

        // A *different* staged version does not mask a newer release.
        let (status, _) = fresh(interpret(available("1.7.0"), Some("1.6.0"), ""));
        assert_eq!(status.state, STATE_AVAILABLE);
        assert_eq!(status.version, "1.7.0");
    }

    #[test]
    fn up_to_date_is_a_fresh_answer_and_keeps_nothing() {
        let (status, update) = fresh(interpret(
            Ok(CheckOutcome::UpToDate {
                latest: "1.5.0".to_owned(),
            }),
            None,
            "",
        ));
        assert_eq!(status.state, STATE_UP_TO_DATE);
        assert!(update.is_none());
    }

    #[test]
    fn no_news_outcomes_do_not_answer_at_all() {
        // Answering here is what would let a second press of Check now retract
        // the offer the first press found.
        for outcome in [Ok(CheckOutcome::NotModified), Ok(CheckOutcome::NotDue)] {
            assert!(matches!(interpret(outcome, None, ""), Verdict::NoNews));
        }
    }

    #[test]
    fn an_offer_outlives_a_check_with_no_news() {
        let offered = UpdateStatus {
            version: "1.6.0".to_owned(),
            notes_url: "https://example.invalid/notes".to_owned(),
            ..UpdateStatus::with_state(STATE_AVAILABLE)
        };
        let kept = no_news(offered, None, "2026-08-16T11:00:00Z");
        assert_eq!(kept.state, STATE_AVAILABLE, "the offer must survive a 304");
        assert_eq!(kept.version, "1.6.0");
        assert_eq!(kept.checked_at, "2026-08-16T11:00:00Z");

        // With nothing to preserve, it falls back to what the disk says.
        let fallback = no_news(UpdateStatus::default(), None, "");
        assert_eq!(fallback.state, STATE_UP_TO_DATE);
        let staged = no_news(UpdateStatus::default(), Some("1.6.0"), "");
        assert_eq!(staged.state, STATE_STAGED);
        assert_eq!(staged.version, "1.6.0");
    }

    #[test]
    fn a_staged_bundle_survives_a_failed_check() {
        // Losing the restart button because a later check could not reach github
        // would strand a download the user has already approved.
        let (status, _) = fresh(interpret(
            Err(UpdateError::Network("host unreachable".to_owned())),
            Some("1.6.0"),
            "",
        ));
        assert_eq!(status.state, STATE_STAGED);
        assert_eq!(status.version, "1.6.0");
        assert!(
            status.message.contains("host unreachable"),
            "the failure is still reported: {}",
            status.message
        );
    }

    #[test]
    fn a_skipped_version_is_its_own_state() {
        let (status, update) = fresh(interpret(
            Ok(CheckOutcome::Skipped {
                version: "1.6.0".to_owned(),
            }),
            None,
            "",
        ));
        assert_eq!(status.state, STATE_SKIPPED);
        assert_eq!(status.version, "1.6.0");
        assert!(update.is_none());
    }

    #[test]
    fn a_failed_check_reports_the_detail_and_drops_any_stale_update() {
        let (status, update) = fresh(interpret(
            Err(UpdateError::Network("host unreachable".to_owned())),
            None,
            "",
        ));
        assert_eq!(status.state, STATE_ERROR);
        assert!(
            status.message.contains("host unreachable"),
            "{}",
            status.message
        );
        assert!(update.is_none());
    }

    #[test]
    fn a_disabled_check_is_not_stamped_as_a_check() {
        let (status, _) = fresh(interpret(
            Ok(CheckOutcome::Disabled),
            None,
            "2026-08-16T10:00:00Z",
        ));
        assert_eq!(status.state, STATE_DISABLED);
        // Nothing reached the network, so nothing was checked.
        assert!(status.checked_at.is_empty());
    }

    #[test]
    fn an_empty_storage_directory_starts_from_nothing_staged() {
        let dir = std::env::temp_dir().join("mbrc-update-service-initial");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        assert_eq!(
            initial_status(&dir.to_string_lossy()),
            UpdateStatus::default()
        );
    }

    #[test]
    fn a_failed_download_still_names_its_update() {
        // "No update could be found" is the wrong thing to tell someone who just
        // pressed Download at an update that is still there to retry.
        let update = match available("1.6.0") {
            Ok(CheckOutcome::Available(update)) => update,
            _ => unreachable!(),
        };
        let status = download_failure(&update, "connection reset");
        assert_eq!(status.state, STATE_DOWNLOAD_FAILED);
        assert_eq!(status.version, "1.6.0");
        assert_eq!(status.message, "connection reset");
    }

    #[test]
    fn the_job_guard_releases_on_drop() {
        let first = Job::claim().expect("nothing else is running");
        assert!(Job::claim().is_none(), "two jobs must not run at once");
        drop(first);
        assert!(Job::claim().is_some(), "the guard did not release");
    }
}
