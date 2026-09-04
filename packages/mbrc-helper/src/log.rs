//! Somewhere for the helper to say what happened.
//!
//! The helper is launched without a console, so every `println!` and `eprintln!`
//! in it goes nowhere. That is not a cosmetic gap: two real bugs - the helper
//! being unable to resolve its own arguments on a packaged install, and being
//! unable to relaunch a packaged MusicBee - both failed here in total silence.
//! MusicBee closed, nothing was applied, and the only signal was an exit code
//! nobody was left running to read.
//!
//! So every line the helper prints is also appended to `mbrc-helper.log` beside
//! the plugin's other logs. Printing continues: it costs nothing when there is no
//! console and it is how the helper is read when run by hand, which is how the
//! first of those bugs was found.
//!
//! Deliberately not a logging framework. The helper is a short-lived process that
//! writes a handful of lines; a dependency, a filter and a level system would all
//! be scaffolding around `writeln!`.

use std::io::Write;
use std::path::{Path, PathBuf};
use std::sync::Mutex;

/// The log's name, beside `mbrc-core.log` in the plugin's storage directory.
const LOG_FILE: &str = "mbrc-helper.log";

/// Where lines are being written, once somewhere has been chosen.
///
/// Resolved on first use rather than at startup because the storage directory is
/// derived from the `--staged` argument, which is not known until argv is parsed
/// - and a failure while parsing argv is exactly the kind worth recording.
static SINK: Mutex<Option<PathBuf>> = Mutex::new(None);

fn sink() -> std::sync::MutexGuard<'static, Option<PathBuf>> {
    SINK.lock().unwrap_or_else(|poisoned| poisoned.into_inner())
}

/// Point the log at the storage directory derived from `staged`.
///
/// `staged` is `<storage>/updates/<version>`, so storage is two levels up - the
/// same derivation `update::backup_root` uses.
///
/// Falls back to the temp directory when that cannot be written, which is not
/// theoretical: a helper started outside its package container cannot see the
/// storage directory at all, and that is the failure this log exists to show.
pub fn direct_to_storage(staged: &Path) {
    *sink() = choose_sink(staged, &std::env::temp_dir());
}

/// Records where this process is running from, which is the question every
/// Store-install failure has turned on.
///
/// A helper running outside a package container resolves `%APPDATA%` literally,
/// and on a machine that also has an ordinary install that path exists - so its
/// lines append to the *other* install's log with nothing to say so. Two
/// installs, one file, no attribution. This line is the attribution.
pub fn note_environment() {
    let identity = match package_family_name() {
        Some(family) => format!("packaged ({family})"),
        None => "unpackaged".to_owned(),
    };
    let sink = match sink().as_ref() {
        Some(path) => path.display().to_string(),
        None => "nowhere (no usable storage or temp directory)".to_owned(),
    };
    line(&format!("running {identity}; logging to {sink}"));
}

/// This process's package family name, or `None` when it has no package
/// identity - the ordinary desktop install, and by far the common case.
#[cfg(windows)]
fn package_family_name() -> Option<String> {
    use windows::Win32::Foundation::{ERROR_INSUFFICIENT_BUFFER, ERROR_SUCCESS};
    use windows::Win32::Storage::Packaging::Appx::GetCurrentPackageFamilyName;
    use windows::core::PWSTR;

    let mut length: u32 = 0;
    // SAFETY: the documented probe - a null buffer with a zero length asks for
    // the required size and writes nothing.
    let probe = unsafe { GetCurrentPackageFamilyName(&mut length, None) };
    // APPMODEL_ERROR_NO_PACKAGE lands here for an unpackaged process, which is a
    // normal answer rather than a failure.
    if probe != ERROR_INSUFFICIENT_BUFFER || length == 0 {
        return None;
    }

    let mut buffer = vec![0u16; length as usize];
    // SAFETY: `buffer` holds `length` units, which is what the probe asked for.
    let result =
        unsafe { GetCurrentPackageFamilyName(&mut length, Some(PWSTR(buffer.as_mut_ptr()))) };
    if result != ERROR_SUCCESS {
        return None;
    }
    // The reported length counts the terminating null.
    let end = (length as usize).saturating_sub(1).min(buffer.len());
    Some(String::from_utf16_lossy(&buffer[..end]))
}

#[cfg(not(windows))]
fn package_family_name() -> Option<String> {
    None
}

/// Where a line would go, given a staging directory and a fallback directory.
///
/// Split from [`direct_to_storage`] so a test need not point the fallback at the
/// real `%TEMP%`, where its fixtures would land in a file users are asked to
/// read. `None` when `staged` is not a staging path at all: the fallback covers
/// a storage directory that cannot be written, not arguments that were never a
/// path.
fn choose_sink(staged: &Path, fallback_dir: &Path) -> Option<PathBuf> {
    let storage = staged.parent().and_then(Path::parent)?;
    if !storage.is_absolute() {
        return None;
    }
    let beside_the_others = storage.join(LOG_FILE);
    if writable(&beside_the_others) {
        return Some(beside_the_others);
    }
    Some(fallback_dir.join(LOG_FILE))
}

/// Records a line, and prints it too.
///
/// Never fails and never panics: the helper is mid-way through replacing a
/// plugin, and losing a log line is not a reason to change what it does.
pub fn line(message: &str) {
    eprintln!("mbrc-helper: {message}");
    let guard = sink();
    let Some(path) = guard.as_ref() else {
        return;
    };
    let _ = append(path, message);
}

fn append(path: &Path, message: &str) -> std::io::Result<()> {
    let mut file = std::fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(path)?;
    writeln!(file, "{} mbrc-helper: {message}", timestamp())
}

/// Whether a line can be appended here, tested by opening rather than by
/// checking the directory: under a package container the answer depends on who is
/// asking, not on what the path looks like.
fn writable(path: &Path) -> bool {
    std::fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(path)
        .is_ok()
}

/// A UTC timestamp in the same shape the core's log uses, so the two read
/// together when both end up in a diagnostics bundle.
fn timestamp() -> String {
    use std::time::{SystemTime, UNIX_EPOCH};
    let now = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0);
    // Civil-from-days, so the helper does not take a date dependency for one line.
    let (days, rem) = (now / 86_400, now % 86_400);
    let (h, m, s) = (rem / 3600, (rem % 3600) / 60, rem % 60);
    let (y, mo, d) = civil_from_days(days as i64);
    format!("{y:04}-{mo:02}-{d:02}T{h:02}:{m:02}:{s:02}Z")
}

/// Howard Hinnant's `civil_from_days`, for days since the Unix epoch.
fn civil_from_days(z: i64) -> (i64, u32, u32) {
    let z = z + 719_468;
    let era = z.div_euclid(146_097);
    let doe = z.rem_euclid(146_097);
    let yoe = (doe - doe / 1460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = (doy - (153 * mp + 2) / 5 + 1) as u32;
    let m = if mp < 10 { mp + 3 } else { mp - 9 } as u32;
    (if m <= 2 { y + 1 } else { y }, m, d)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Serializes the tests that move the sink, which is process-global because
    /// the helper has exactly one log. Without this they race over it.
    static TEST_GUARD: Mutex<()> = Mutex::new(());

    fn exclusive() -> std::sync::MutexGuard<'static, ()> {
        TEST_GUARD
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
    }

    fn scratch_root(case: &str) -> PathBuf {
        std::env::temp_dir().join(format!("mbrc-helper-log-{case}"))
    }

    fn scratch(case: &str) -> PathBuf {
        let dir = scratch_root(case);
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("create scratch dir");
        dir
    }

    #[test]
    fn writes_beside_the_other_logs() {
        let _exclusive = exclusive();
        let storage = scratch("storage");
        let staged = storage.join("updates").join("1.5.0");
        std::fs::create_dir_all(&staged).expect("create staged dir");

        direct_to_storage(&staged);
        line("applied 1.5.0 (3 file(s))");

        let body = std::fs::read_to_string(storage.join(LOG_FILE)).expect("log written");
        assert!(body.contains("applied 1.5.0"), "{body}");
        // Timestamped, so two runs can be told apart.
        assert!(body.starts_with("20"), "{body}");

        *sink() = None;
        let _ = std::fs::remove_dir_all(&storage);
    }

    #[test]
    fn falls_back_when_the_storage_directory_is_unreachable() {
        // A scratch fallback rather than the real temp directory, so the suite
        // never writes into a log a user is actually asked to read.
        let fallback = scratch("fallback");
        // Absolute but nonexistent on either platform: a Windows-shaped literal
        // would take the "never a path" branch and test nothing.
        let staged = scratch("unreachable")
            .join("no-such-directory")
            .join("mb_remote")
            .join("updates")
            .join("1.5.0");

        assert_eq!(
            choose_sink(&staged, &fallback),
            Some(fallback.join(LOG_FILE)),
            "an unreachable storage directory must not silence the log"
        );

        let _ = std::fs::remove_dir_all(&fallback);
        let _ = std::fs::remove_dir_all(scratch_root("unreachable"));
    }

    #[test]
    fn prefers_the_storage_directory_when_it_is_writable() {
        let storage = scratch("prefers");
        let staged = storage.join("updates").join("1.5.0");
        std::fs::create_dir_all(&staged).expect("create staged dir");
        let fallback = scratch("prefers-fallback");

        assert_eq!(
            choose_sink(&staged, &fallback),
            Some(storage.join(LOG_FILE))
        );

        let _ = std::fs::remove_dir_all(&storage);
        let _ = std::fs::remove_dir_all(&fallback);
    }

    #[test]
    fn an_argument_that_was_never_a_path_gets_no_log_file() {
        // What the argv tests pass. Falling back for these is what put test
        // fixtures into the real %TEMP%/mbrc-helper.log.
        let fallback = scratch("never-a-path");
        assert_eq!(choose_sink(Path::new("a"), &fallback), None);
        assert_eq!(choose_sink(Path::new("updates/1.5.0"), &fallback), None);
        let _ = std::fs::remove_dir_all(&fallback);
    }

    #[test]
    fn logging_before_a_sink_is_chosen_is_harmless() {
        let _exclusive = exclusive();
        // argv is parsed before --staged is known, and a failure there is worth
        // printing even though it cannot be filed yet.
        *sink() = None;
        line("refusing to continue: --staged is empty");
    }

    #[test]
    fn the_timestamp_is_a_real_date() {
        const DAYS_TO_22_AUG_2026: i64 = 20_687;
        const DAYS_TO_LEAP_DAY_2024: i64 = 19_782;

        assert_eq!(civil_from_days(DAYS_TO_22_AUG_2026), (2026, 8, 22));
        assert_eq!(civil_from_days(0), (1970, 1, 1), "epoch day zero");
        assert_eq!(civil_from_days(DAYS_TO_LEAP_DAY_2024), (2024, 2, 29));
    }
}
