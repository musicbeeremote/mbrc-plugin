//! The capture session: one at a time, owned by the core.
//!
//! A capture raises the log level to debug for the session only, remembers where
//! in the log it began, and ends by writing a bundle sliced to that window. The
//! core owns it rather than the host because there is no transient-setting
//! concept on the C# side: the only way it can change the log level is a
//! `WriteSettings` that persists it, and a level pushed without one is reverted
//! by the next core reload.
//!
//! Two rules keep it honest:
//!
//! - **One capture at a time.** A second start while one runs is refused, not
//!   queued - two overlapping windows would make the offset a lie.
//! - **A capture always ends.** A safety auto-stop restores the level after
//!   [`MAX_CAPTURE`], so a user who forgets does not leave the plugin writing
//!   debug logs until MusicBee closes.
//!
//! The session survives a MusicBee restart (it is recorded in `capture.json`),
//! because "it breaks on startup" is exactly the bug that most needs capturing
//! and the log file is appended across restarts rather than truncated.

use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Mutex, MutexGuard};
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use serde::{Deserialize, Serialize};

use crate::config::LogLevel;
use crate::diagnostics::{bundle, report};
use crate::ffi::dtos::{CaptureEnvEntry, CaptureRequest, CaptureStatus};
use crate::ffi::types::{HostEventType, MbrcResult};
use crate::state::Core;

/// Nothing is being captured and nothing has been this session.
pub const CAPTURE_IDLE: &str = "idle";
/// A capture is running; the log is at debug level.
pub const CAPTURE_CAPTURING: &str = "capturing";
/// The capture ended and the bundle is being written.
pub const CAPTURE_WRITING: &str = "writing";
/// A bundle was written; `bundle_path` names it.
pub const CAPTURE_DONE: &str = "done";
/// The safety auto-stop ended a capture nobody stopped.
pub const CAPTURE_EXPIRED: &str = "expired";
/// The capture or the bundle failed; `message` says how, and so does the log.
pub const CAPTURE_ERROR: &str = "error";

/// How long a capture may run before it stops itself. Long enough to reproduce
/// anything a user can reproduce on purpose, short enough that a forgotten
/// capture cannot fill the 10 MiB x 3 log budget with debug output.
const MAX_CAPTURE: Duration = Duration::from_secs(30 * 60);

/// How often the watchdog re-checks. Coarse on purpose: it exists to bound a
/// forgotten capture, not to stop one on the second.
const WATCHDOG_TICK: Duration = Duration::from_secs(5);

/// Where a running capture is recorded, so it survives a MusicBee restart.
const CAPTURE_FILE: &str = "capture.json";

/// A running capture.
struct Session {
    /// Distinguishes this capture from any later one, so a watchdog thread whose
    /// capture already ended does nothing when it wakes.
    generation: u64,
    started_unix_ms: i64,
    /// Monotonic start, for the auto-stop. Separate from `started_unix_ms`
    /// because a wall-clock jump must not shorten or extend a capture.
    started: Instant,
    log_offset: u64,
    host_environment: Vec<CaptureEnvEntry>,
}

/// What a capture leaves behind once it ends, plus the running one if any.
struct State {
    session: Option<Session>,
    /// The state to report when no session is running.
    resting: &'static str,
    bundle_path: String,
    message: String,
}

static STATE: Mutex<State> = Mutex::new(State {
    session: None,
    resting: CAPTURE_IDLE,
    bundle_path: String::new(),
    message: String::new(),
});

static GENERATION: AtomicU64 = AtomicU64::new(0);

fn lock() -> MutexGuard<'static, State> {
    STATE
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner())
}

/// The on-disk record of a running capture. Only what a restart cannot
/// reconstruct: the wall-clock start, the offset, and what the host told us.
#[derive(Serialize, Deserialize)]
struct Persisted {
    started_unix_ms: i64,
    log_offset: u64,
    #[serde(default)]
    host_environment: Vec<CaptureEnvEntry>,
}

/// Begins a capture. Refused while one is already running.
pub fn start(core: &Core, request: CaptureRequest) -> MbrcResult {
    let storage = core.config.storage_path.clone();
    let mut guard = lock();
    if guard.session.is_some() {
        return MbrcResult::AlreadyRunning;
    }

    let session = Session {
        generation: GENERATION.fetch_add(1, Ordering::AcqRel) + 1,
        started_unix_ms: now_unix_ms(),
        started: Instant::now(),
        log_offset: log_len(&storage),
        host_environment: request.host_environment,
    };
    let generation = session.generation;
    persist(&storage, &session);
    guard.session = Some(session);
    guard.resting = CAPTURE_IDLE;
    guard.message = String::new();
    drop(guard);

    raise_level(core);
    tracing::info!("diagnostics capture started");
    spawn_watchdog(generation, MAX_CAPTURE);
    emit(core);
    MbrcResult::Ok
}

/// Ends the capture and writes the bundle in the background.
///
/// The write is off-thread for the same reason the update flow's are: the host
/// calls this from MusicBee's UI thread, and zipping tens of megabytes of log
/// would freeze it. The panel learns the outcome from the status event.
pub fn stop(core: std::sync::Arc<Core>, request: &CaptureRequest) -> MbrcResult {
    let mut guard = lock();
    let Some(session) = guard.session.take() else {
        return MbrcResult::InvalidArgument;
    };
    guard.resting = CAPTURE_WRITING;
    drop(guard);

    // Back to the user's chosen level first: whatever happens to the bundle, the
    // capture itself is over.
    restore_level(&core);
    clear_persisted(&core.config.storage_path);
    emit(&core);

    let destination = request.destination_dir.clone();
    std::thread::spawn(move || {
        // Checked before the build, not just before the event: zipping takes
        // seconds, and the host's delegates go away at `mbrc_shutdown`.
        if !crate::state::is_initialized() {
            tracing::warn!("skipping the diagnostics bundle: the core shut down first");
            lock().resting = CAPTURE_IDLE;
            return;
        }
        finish(&core, &session, &destination);
    });
    MbrcResult::Ok
}

/// Builds the bundle and records the outcome. Split out of the thread body in
/// [`stop`] so the shutdown guard and the work it guards can be exercised
/// separately - a test builds a `Core` directly rather than through
/// `state::initialize`, so it never satisfies that guard.
fn finish(core: &Core, session: &Session, destination: &str) {
    let outcome = build_bundle(core, session, destination);
    let mut guard = lock();
    match outcome {
        Ok(path) => {
            tracing::info!(path = %path, "diagnostics bundle written");
            guard.resting = CAPTURE_DONE;
            guard.bundle_path = path;
            guard.message = String::new();
        }
        Err(error) => {
            tracing::error!(error = %error, "diagnostics bundle failed");
            guard.resting = CAPTURE_ERROR;
            guard.message = error;
        }
    }
    drop(guard);
    // Re-checked: the build is the slow part, so a shutdown is likelier to have
    // landed by now than it was before it.
    if crate::state::is_initialized() {
        emit(core);
    }
}

/// Abandons the capture: restores the level and writes nothing.
pub fn cancel(core: &Core) -> MbrcResult {
    let mut guard = lock();
    if guard.session.take().is_none() {
        return MbrcResult::InvalidArgument;
    }
    guard.resting = CAPTURE_IDLE;
    guard.message = String::new();
    drop(guard);

    restore_level(core);
    clear_persisted(&core.config.storage_path);
    tracing::info!("diagnostics capture cancelled");
    emit(core);
    MbrcResult::Ok
}

/// The current status. Always an answer, even before anything has run.
pub fn status() -> CaptureStatus {
    let guard = lock();
    match &guard.session {
        Some(session) => CaptureStatus {
            state: CAPTURE_CAPTURING.to_owned(),
            started_unix_ms: session.started_unix_ms,
            seconds_remaining: MAX_CAPTURE
                .saturating_sub(session.started.elapsed())
                .as_secs() as i32,
            bundle_path: guard.bundle_path.clone(),
            message: String::new(),
        },
        None => CaptureStatus {
            state: guard.resting.to_owned(),
            started_unix_ms: 0,
            seconds_remaining: 0,
            bundle_path: guard.bundle_path.clone(),
            message: guard.message.clone(),
        },
    }
}

/// Serializes the status as MessagePack for the settings panel.
pub fn status_bytes() -> Option<Vec<u8>> {
    rmp_serde::to_vec_named(&status()).ok()
}

/// Resumes a capture that a MusicBee restart interrupted.
///
/// Called once from init. The log is appended across restarts rather than
/// truncated, so the original offset still points at the start of the window -
/// which is the whole reason a startup bug can be captured at all. A record
/// older than [`MAX_CAPTURE`] is dropped rather than resumed into an instant
/// expiry, and a session that is already live is left alone: a port change
/// re-inits the core without clearing this module's state.
pub fn resume_after_restart(core: &Core) {
    // Scoped so the guard is released before the lock is taken again below:
    // this mutex is not reentrant.
    {
        let guard = lock();
        if guard.session.is_some() {
            return;
        }
    }

    let storage = &core.config.storage_path;
    let Some(persisted) = read_persisted(storage) else {
        return;
    };
    let age_ms = now_unix_ms()
        .saturating_sub(persisted.started_unix_ms)
        .max(0);
    let remaining = MAX_CAPTURE.saturating_sub(Duration::from_millis(age_ms as u64));
    if remaining.is_zero() {
        tracing::info!("dropping a diagnostics capture that outlived its window");
        clear_persisted(storage);
        return;
    }

    let session = Session {
        generation: GENERATION.fetch_add(1, Ordering::AcqRel) + 1,
        started_unix_ms: persisted.started_unix_ms,
        started: rebased_start(remaining),
        log_offset: persisted.log_offset,
        host_environment: persisted.host_environment,
    };
    let generation = session.generation;
    let mut guard = lock();
    guard.session = Some(session);
    guard.resting = CAPTURE_IDLE;
    drop(guard);

    raise_level(core);
    tracing::info!(
        remaining_secs = remaining.as_secs(),
        "resumed a diagnostics capture across a restart"
    );
    spawn_watchdog(generation, remaining);
}

/// Assembles the report and hands it to the bundle writer.
/// The start instant of a resumed capture, rebased onto this process's clock.
///
/// The time already spent is taken off, so the auto-stop fires [`MAX_CAPTURE`]
/// after the capture began rather than after the restart. `checked_sub` because
/// a Windows `Instant` counts from boot and the restart is often a reboot:
/// taking four minutes of capture off a machine up for one would panic inside
/// `mbrc_initialize` and report the plugin as failed to start.
fn rebased_start(remaining: Duration) -> Instant {
    Instant::now()
        .checked_sub(MAX_CAPTURE - remaining)
        .unwrap_or_else(Instant::now)
}

fn build_bundle(core: &Core, session: &Session, destination: &str) -> Result<String, String> {
    if destination.trim().is_empty() {
        return Err("no destination folder was given for the bundle".to_owned());
    }
    let report = report::build(core, &session.host_environment, session.started_unix_ms);
    let path = bundle::write(bundle::Inputs {
        storage: &core.config.storage_path,
        destination_dir: destination,
        report,
        log_offset: session.log_offset,
        started_unix_ms: session.started_unix_ms,
    })?;
    Ok(path.to_string_lossy().into_owned())
}

/// Ends a capture that nobody stopped, restoring the log level.
///
/// Deliberately does not write a bundle: there is no destination to write it to
/// (the host supplies that on stop), and a user who walked away is not waiting
/// for a file. The window is still in the log if they want to try again.
fn expire(generation: u64) {
    let mut guard = lock();
    match &guard.session {
        // A newer capture, or none - this watchdog outlived its session.
        Some(session) if session.generation != generation => return,
        None => return,
        Some(_) => {}
    }
    guard.session = None;
    guard.resting = CAPTURE_EXPIRED;
    guard.message = format!(
        "The capture stopped itself after {} minutes and the log level went back to normal.",
        MAX_CAPTURE.as_secs() / 60
    );
    drop(guard);

    // After a shutdown there is no core to restore the level on, and no host to
    // tell - the process is going away with it either way.
    if let Some(core) = crate::state::core_handle() {
        restore_level(&core);
        clear_persisted(&core.config.storage_path);
        tracing::warn!("diagnostics capture stopped itself after reaching its time limit");
        emit(&core);
    }
}

/// Watches a capture and expire it once `remaining` has passed.
///
/// A thread rather than a lazy check on the next query: with the panel closed
/// nothing queries, and the whole point is to bound a capture the user forgot.
fn spawn_watchdog(generation: u64, remaining: Duration) {
    std::thread::spawn(move || {
        let deadline = Instant::now() + remaining;
        loop {
            std::thread::sleep(WATCHDOG_TICK);
            // Cheap early exit for the normal case: the capture was stopped, so
            // this thread has nothing left to guard.
            let still_running = {
                let guard = lock();
                matches!(&guard.session, Some(s) if s.generation == generation)
            };
            if !still_running {
                return;
            }
            if Instant::now() >= deadline {
                expire(generation);
                return;
            }
        }
    });
}

/// Raises the log level for the capture, without ever lowering it: a user already
/// running at trace asked for more than debug, and a capture should not take it
/// away.
fn raise_level(core: &Core) {
    let level = match core.config.log_level {
        LogLevel::Trace => LogLevel::Trace,
        _ => LogLevel::Debug,
    };
    apply_level(level);
}

/// Puts the level back to whatever the user has saved.
fn restore_level(core: &Core) {
    apply_level(core.config.log_level);
}

fn apply_level(level: LogLevel) {
    if let Err(error) = crate::logging::set_level(directive_for(level)) {
        tracing::warn!(error = %error, "could not change the log level for the capture");
    }
}

/// The tracing directive for a settings log level. Mirrors the host's own
/// mapping in `NativeBridge.SetLogLevel`, so a level set here and a level set
/// there mean the same thing.
fn directive_for(level: LogLevel) -> &'static str {
    match level {
        LogLevel::Trace => "info,mbrc_core=trace,mbrc=trace",
        LogLevel::Debug => "info,mbrc_core=debug,mbrc=debug",
        LogLevel::Info => "mbrc_core=info,info",
    }
}

/// Tells an open panel to re-query. Best effort: no host callback, no event.
fn emit(core: &Core) {
    core.providers
        .emit_event(HostEventType::CaptureStatusChanged, &[]);
}

/// Current length of the active log - where a capture's window begins.
fn log_len(storage: &str) -> u64 {
    std::fs::metadata(crate::logging::active_log_path(storage))
        .map(|meta| meta.len())
        .unwrap_or(0)
}

fn capture_file(storage: &str) -> std::path::PathBuf {
    std::path::Path::new(storage).join(CAPTURE_FILE)
}

/// Records the running capture. A failure here is logged and tolerated: it costs
/// the restart-survival, not the capture.
fn persist(storage: &str, session: &Session) {
    if storage.is_empty() {
        return;
    }
    let record = Persisted {
        started_unix_ms: session.started_unix_ms,
        log_offset: session.log_offset,
        host_environment: session.host_environment.clone(),
    };
    let written = serde_json::to_string_pretty(&record)
        .map_err(|e| e.to_string())
        .and_then(|body| std::fs::write(capture_file(storage), body).map_err(|e| e.to_string()));
    if let Err(error) = written {
        tracing::warn!(error = %error, "could not record the capture; it will not survive a restart");
    }
}

fn read_persisted(storage: &str) -> Option<Persisted> {
    if storage.is_empty() {
        return None;
    }
    let body = std::fs::read_to_string(capture_file(storage)).ok()?;
    match serde_json::from_str(&body) {
        Ok(record) => Some(record),
        Err(error) => {
            tracing::warn!(error = %error, "ignoring an unreadable capture record");
            let _ = std::fs::remove_file(capture_file(storage));
            None
        }
    }
}

fn clear_persisted(storage: &str) {
    if storage.is_empty() {
        return;
    }
    let _ = std::fs::remove_file(capture_file(storage));
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

    /// Serializes the tests that drive the module's global session state.
    ///
    /// `STATE` is deliberately process-global - a plugin runs one capture, not
    /// one per caller - so tests that start, stop or expire a capture fight over
    /// it when cargo runs them on separate threads. Same hazard `state.rs` notes
    /// about its own global; it folds its cases into one test, this takes a lock
    /// so the cases keep their own names.
    static TEST_GUARD: Mutex<()> = Mutex::new(());

    fn exclusive() -> MutexGuard<'static, ()> {
        TEST_GUARD
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
    }

    /// Resets the module's global between tests, which share the process.
    fn reset() {
        let mut guard = lock();
        guard.session = None;
        guard.resting = CAPTURE_IDLE;
        guard.bundle_path = String::new();
        guard.message = String::new();
    }

    #[test]
    fn status_is_idle_before_anything_runs() {
        let _exclusive = exclusive();
        reset();
        let status = status();
        assert_eq!(status.state, CAPTURE_IDLE);
        assert_eq!(status.started_unix_ms, 0);
        assert_eq!(status.seconds_remaining, 0);
    }

    #[test]
    fn status_round_trips_as_named_msgpack() {
        let _exclusive = exclusive();
        // `to_vec` would write a positional fixarray and break the panel at
        // runtime, which no serde round-trip would catch.
        reset();
        let bytes = status_bytes().expect("status serializes");
        let back: CaptureStatus = rmp_serde::from_slice(&bytes).expect("named map decodes");
        assert_eq!(back.state, CAPTURE_IDLE);
        // A map, not an array: msgpack fixmap codes are 0x80..=0x8f.
        assert!(
            (0x80..=0x8f).contains(&bytes[0]),
            "expected a fixmap, got {:#04x}",
            bytes[0]
        );
    }

    #[test]
    fn request_round_trips_as_named_msgpack() {
        let request = CaptureRequest {
            destination_dir: "C:\\Users\\someone\\Desktop".to_owned(),
            host_environment: vec![CaptureEnvEntry {
                key: "musicbee_build".to_owned(),
                value: "3.6.8859".to_owned(),
            }],
        };
        let bytes = rmp_serde::to_vec_named(&request).expect("request serializes");
        let back: CaptureRequest = rmp_serde::from_slice(&bytes).expect("named map decodes");
        assert_eq!(back.destination_dir, request.destination_dir);
        assert_eq!(back.host_environment[0].key, "musicbee_build");
        assert_eq!(back.host_environment[0].value, "3.6.8859");
    }

    #[test]
    fn a_capture_raises_the_level_but_never_lowers_it() {
        // Trace is more than debug; a capture must not take it away.
        assert_eq!(
            directive_for(LogLevel::Trace),
            "info,mbrc_core=trace,mbrc=trace"
        );
        assert_eq!(
            directive_for(LogLevel::Debug),
            "info,mbrc_core=debug,mbrc=debug"
        );
        assert_eq!(directive_for(LogLevel::Info), "mbrc_core=info,info");
    }

    #[test]
    fn expiring_a_stale_generation_leaves_a_newer_capture_alone() {
        let _exclusive = exclusive();
        reset();
        let mut guard = lock();
        guard.session = Some(Session {
            generation: 42,
            started_unix_ms: now_unix_ms(),
            started: Instant::now(),
            log_offset: 0,
            host_environment: Vec::new(),
        });
        drop(guard);

        // A watchdog from an earlier capture waking up late.
        expire(41);
        assert_eq!(status().state, CAPTURE_CAPTURING);

        reset();
    }

    #[test]
    fn the_persisted_record_round_trips() {
        let dir = std::env::temp_dir().join("mbrc-capture-persist");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("create scratch dir");
        let storage = dir.to_str().expect("utf8 dir");

        let session = Session {
            generation: 1,
            started_unix_ms: 1_700_000_000_000,
            started: Instant::now(),
            log_offset: 4096,
            host_environment: vec![CaptureEnvEntry {
                key: "os".to_owned(),
                value: "Windows 11".to_owned(),
            }],
        };
        persist(storage, &session);

        let back = read_persisted(storage).expect("record reads back");
        assert_eq!(back.started_unix_ms, 1_700_000_000_000);
        assert_eq!(back.log_offset, 4096);
        assert_eq!(back.host_environment[0].value, "Windows 11");

        clear_persisted(storage);
        assert!(read_persisted(storage).is_none());
        let _ = std::fs::remove_dir_all(&dir);
    }

    /// Drives a whole capture against a real `Core` on a scratch storage dir:
    /// start, log something, stop, and read the bundle back. The one test that
    /// proves the pieces fit together rather than testing each in isolation.
    #[test]
    fn a_capture_start_to_bundle_produces_a_readable_zip() {
        let _exclusive = exclusive();
        use crate::config::Config;
        use crate::providers::NullProviders;
        use std::sync::Arc;

        reset();
        let dir = std::env::temp_dir().join("mbrc-capture-endtoend");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("create scratch dir");
        let storage = dir.to_str().expect("utf8 dir").to_owned();

        // A line from before the capture, for the window to exclude.
        std::fs::write(
            crate::logging::active_log_path(&storage),
            "before the capture\n",
        )
        .expect("seed log");

        let config = Config {
            storage_path: storage.clone(),
            // Never 0.0.0.0 in a test: it raises a firewall prompt per binary.
            ..Config::for_test(0)
        };
        let core = Arc::new(Core::new(Arc::new(NullProviders), config));

        let environment = vec![CaptureEnvEntry {
            key: "musicbee_build".to_owned(),
            value: "3.6.8859".to_owned(),
        }];
        assert_eq!(
            start(
                &core,
                CaptureRequest {
                    destination_dir: String::new(),
                    host_environment: environment,
                }
            ),
            MbrcResult::Ok,
            "a capture should start on an idle core"
        );
        assert_eq!(status().state, CAPTURE_CAPTURING);
        assert_eq!(
            start(&core, CaptureRequest::default()),
            MbrcResult::AlreadyRunning
        );

        let started = status().started_unix_ms;
        let offset = "before the capture
"
        .len() as u64;

        // A log line written inside the capture window.
        {
            use std::io::Write as _;
            let mut log = std::fs::OpenOptions::new()
                .append(true)
                .open(crate::logging::active_log_path(&storage))
                .expect("append to log");
            writeln!(log, "during the capture").expect("write log line");
        }

        let out = dir.join("desktop");
        // Stop carries only the destination, as the host does.
        let request = CaptureRequest {
            destination_dir: out.to_str().expect("utf8 dir").to_owned(),
            host_environment: Vec::new(),
        };
        assert_eq!(stop(core.clone(), &request), MbrcResult::Ok);

        // An uninitialized `Core` means `stop`'s thread returns without
        // building, so drive the work half directly.
        let session = Session {
            generation: 0,
            started_unix_ms: started,
            started: Instant::now(),
            log_offset: offset,
            host_environment: vec![CaptureEnvEntry {
                key: "musicbee_build".to_owned(),
                value: "3.6.8859".to_owned(),
            }],
        };
        finish(&core, &session, &request.destination_dir);

        let settled = status();
        assert_eq!(
            settled.state, CAPTURE_DONE,
            "bundle did not finish: {}",
            settled.message
        );

        let file = std::fs::File::open(&settled.bundle_path).expect("bundle exists");
        let mut archive = zip::ZipArchive::new(file).expect("bundle is a zip");
        let mut window = String::new();
        {
            use std::io::Read as _;
            archive
                .by_name("capture.log")
                .expect("capture.log present")
                .read_to_string(&mut window)
                .expect("read capture.log");
        }
        assert_eq!(
            window, "during the capture\n",
            "the window should exclude what was logged before the capture"
        );

        let mut body = String::new();
        {
            use std::io::Read as _;
            archive
                .by_name("report.json")
                .expect("report.json present")
                .read_to_string(&mut body)
                .expect("read report.json");
        }
        let report: serde_json::Value = serde_json::from_str(&body).expect("report parses");
        assert_eq!(
            report["capture"]["host_environment"]["musicbee_build"],
            "3.6.8859"
        );
        assert_eq!(report["listening"]["port"], 0);
        assert_eq!(
            report["settings"]["redacted_keys"],
            serde_json::json!(["allowed_addresses"])
        );

        reset();
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_bundle_is_skipped_when_the_core_shut_down() {
        let _exclusive = exclusive();
        // A test `Core` is never in the global state, which is the shut-down
        // case: the report reads the plugin version back through the host.
        use crate::config::Config;
        use crate::providers::NullProviders;
        use std::sync::Arc;

        reset();
        assert!(
            !crate::state::is_initialized(),
            "this test needs an uninitialized global core"
        );

        let dir = std::env::temp_dir().join("mbrc-capture-shutdown");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("create scratch dir");
        let storage = dir.to_str().expect("utf8 dir").to_owned();
        let config = Config {
            storage_path: storage,
            ..Config::for_test(0)
        };
        let core = Arc::new(Core::new(Arc::new(NullProviders), config));

        assert_eq!(start(&core, CaptureRequest::default()), MbrcResult::Ok);
        let out = dir.join("desktop");
        let request = CaptureRequest {
            destination_dir: out.to_str().expect("utf8 dir").to_owned(),
            host_environment: Vec::new(),
        };
        assert_eq!(stop(core.clone(), &request), MbrcResult::Ok);

        // The thread should bail out rather than write anything.
        let mut settled = status();
        for _ in 0..100 {
            if settled.state != CAPTURE_WRITING {
                break;
            }
            std::thread::sleep(Duration::from_millis(20));
            settled = status();
        }
        assert_eq!(settled.state, CAPTURE_IDLE);
        assert!(
            !out.exists(),
            "nothing should have been written after a shutdown"
        );

        reset();
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn resuming_does_not_replay_over_a_live_capture() {
        let _exclusive = exclusive();
        // A settings save that changes the port re-inits the core in-process, so
        // resume runs again while a capture is genuinely still going.
        use crate::config::Config;
        use crate::providers::NullProviders;
        use std::sync::Arc;

        reset();
        let dir = std::env::temp_dir().join("mbrc-capture-reinit");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("create scratch dir");
        let storage = dir.to_str().expect("utf8 dir").to_owned();

        let config = Config {
            storage_path: storage.clone(),
            ..Config::for_test(0)
        };
        let core = Arc::new(Core::new(Arc::new(NullProviders), config));

        assert_eq!(start(&core, CaptureRequest::default()), MbrcResult::Ok);
        let started = status().started_unix_ms;
        let generation_before = GENERATION.load(Ordering::Acquire);

        // The re-init path. The persisted record is still on disk.
        resume_after_restart(&core);

        assert_eq!(
            GENERATION.load(Ordering::Acquire),
            generation_before,
            "resume must not bump the generation over a live capture (a second watchdog)"
        );
        let after = status();
        assert_eq!(after.state, CAPTURE_CAPTURING);
        assert_eq!(
            after.started_unix_ms, started,
            "the live capture's start must survive a re-init"
        );

        cancel(&core);
        reset();
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn an_unreadable_record_is_discarded_not_fatal() {
        let dir = std::env::temp_dir().join("mbrc-capture-garbage");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("create scratch dir");
        let storage = dir.to_str().expect("utf8 dir");
        std::fs::write(capture_file(storage), "{ not json").expect("seed garbage");

        assert!(read_persisted(storage).is_none());
        // And it cleans up after itself, so the next start is not haunted by it.
        assert!(!capture_file(storage).exists());
        let _ = std::fs::remove_dir_all(&dir);
    }
}
