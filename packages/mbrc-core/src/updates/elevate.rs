//! Handing a staged update to the elevated helper.
//!
//! The panel presses a button; this decides whether elevation is needed, proves
//! the helper it is about to run is the one the release signed, and launches it.
//!
//! Four things here are not arbitrary:
//!
//! - **Nothing is taken from the caller.** The plugins directory is where this
//!   very DLL was loaded from, MusicBee is this process, and the pid is our own.
//!   A panel that could name the directory to overwrite would be a panel worth
//!   attacking; there is nothing to pass, so there is nothing to tamper with.
//! - **The staged helper is verified before it is *executed*, and cannot be
//!   swapped in between.** It runs elevated out of a user-writable directory, so
//!   its signature check is the security boundary, and it lands earlier than the
//!   check the helper performs on the DLLs. Verifying by path and then launching
//!   by path would leave a window: any process running as this user could
//!   replace the file after it verified and have *its* binary run as
//!   administrator, on the prompt the user was expecting. So the file is opened
//!   denying write and delete sharing, verified through that handle, and the
//!   handle is held open across the launch. The installed helper cannot be used
//!   instead: a release replaces `mbrc-helper.exe` too, and a running image
//!   cannot overwrite itself.
//! - **A staged bundle that is not newer is refused.** Every release is public
//!   and legitimately signed, so a signature alone does not make a bundle the
//!   right one to install: without this, anyone who can write to the staging
//!   directory could roll the plugin back to an older release - with a valid
//!   signature - and undo whatever the newer one fixed.
//! - **Elevation is asked for now, not later.** `runas` prompts while the user is
//!   still looking at the button they pressed. If the helper self-elevated after
//!   MusicBee had exited, a declined prompt would leave a closed MusicBee, no UI,
//!   and nothing to report the cancellation into.

use std::path::{Path, PathBuf};

use mbrc_release::{stage::STAGING_DIR, verify_bundled_file, verify_manifest, Manifest};

use crate::ffi::types::UpdateLaunch;

/// The helper, as named in the manifest and staged beside the DLLs.
const HELPER_EXE: &str = "mbrc-helper.exe";
const MANIFEST_ASSET: &str = "manifest.json";
const SIGNATURE_ASSET: &str = "manifest.json.minisig";

/// Verifies the staged bundle's helper and launches it, elevating if the plugins
/// directory is not writable as we stand.
///
/// Returns the outcome the panel reports; the detail goes to the log, because a
/// UI that has to render every failure mode is a UI that renders none of them
/// well.
pub fn launch(storage_path: &str, current_version: &str) -> UpdateLaunch {
    let pending = match mbrc_release::read_pending(storage_path) {
        Ok(Some(pending)) => pending,
        Ok(None) => return UpdateLaunch::NothingStaged,
        Err(e) => {
            tracing::warn!(error = %e, "the staged-update marker is unreadable");
            return UpdateLaunch::NothingStaged;
        }
    };

    let staged = Path::new(storage_path)
        .join(STAGING_DIR)
        .join(&pending.version);
    let helper = match verified_helper(&staged) {
        Ok(helper) => helper,
        Err(e) => {
            tracing::error!(error = %e, version = %pending.version, "refusing to run the staged helper");
            return UpdateLaunch::VerifyFailed;
        }
    };

    // Signed, but is it *newer*? Every release is public and signed, so a stale
    // bundle staged by someone else carries a perfectly good signature.
    if !is_upgrade(&helper.version, current_version) {
        tracing::error!(
            staged = %helper.version,
            installed = current_version,
            "refusing to apply a staged update that is not newer than what is installed"
        );
        return UpdateLaunch::NotAnUpgrade;
    }

    let target = match plugins_dir() {
        Some(dir) => dir,
        None => {
            tracing::error!("could not determine the plugins directory");
            return UpdateLaunch::Failed;
        }
    };
    let relaunch = match std::env::current_exe() {
        Ok(exe) => exe,
        Err(e) => {
            tracing::error!(error = %e, "could not determine the MusicBee executable");
            return UpdateLaunch::Failed;
        }
    };

    let elevate = !is_writable(&target);
    tracing::info!(
        version = %helper.version,
        helper = %helper.path.display(),
        target = %target.display(),
        elevate,
        "launching the update helper"
    );

    let outcome = spawn(
        &helper.path,
        &arguments(&staged, &target, &relaunch),
        elevate,
    );
    // Explicit, so it is obvious that the handle is what has been holding the
    // verified bytes in place all the way through the launch.
    drop(helper);
    outcome
}

/// Whether `staged` is newer than what is installed.
///
/// Unparseable on either side is not an upgrade: a version that cannot be
/// compared cannot be shown to be newer, and this is the last gate before an
/// elevated file swap.
fn is_upgrade(staged: &str, installed: &str) -> bool {
    match (
        mbrc_release::version::parse(staged),
        mbrc_release::version::parse(installed),
    ) {
        (Ok(staged), Ok(installed)) => staged > installed,
        (staged, installed) => {
            tracing::warn!(
                staged_ok = staged.is_ok(),
                installed_ok = installed.is_ok(),
                "could not compare the staged and installed versions"
            );
            false
        }
    }
}

/// A staged helper that has verified, with the handle that keeps it that way.
///
/// Holding `_handle` open is what makes the verification mean anything at launch
/// time: it denies write and delete sharing, so between the hash check and
/// `ShellExecuteExW` the file cannot be rewritten, renamed or replaced.
struct VerifiedHelper {
    path: PathBuf,
    version: String,
    _handle: std::fs::File,
}

/// The staged helper, once the release signature says it is the right bytes.
///
/// Verified from the *staged* manifest and signature, not from anything the core
/// remembers about the download: the point is to check the file that is about to
/// run as administrator, now, in the state it is in on disk - and, via the
/// handle, to keep that from changing before it does.
fn verified_helper(staged: &Path) -> Result<VerifiedHelper, String> {
    let manifest_bytes =
        std::fs::read(staged.join(MANIFEST_ASSET)).map_err(|e| format!("{MANIFEST_ASSET}: {e}"))?;
    let signature = std::fs::read_to_string(staged.join(SIGNATURE_ASSET))
        .map_err(|e| format!("{SIGNATURE_ASSET}: {e}"))?;
    let (manifest, key) =
        verify_manifest(&manifest_bytes, &signature).map_err(|e| e.to_string())?;

    let path = staged.join(HELPER_EXE);
    let mut handle = open_exclusive(&path).map_err(|e| format!("{HELPER_EXE}: {e}"))?;
    let mut bytes = Vec::new();
    std::io::Read::read_to_end(&mut handle, &mut bytes)
        .map_err(|e| format!("{HELPER_EXE}: {e}"))?;
    verify_bundled_file(&manifest, HELPER_EXE, &bytes).map_err(|e| e.to_string())?;
    log_verified(&manifest, key);
    Ok(VerifiedHelper {
        path,
        version: manifest.version,
        _handle: handle,
    })
}

/// Opens a file for reading while denying write and delete sharing.
///
/// Execution is still allowed: Windows counts `FILE_EXECUTE` as read access when
/// it checks sharing, so the loader can map the image while this handle is open.
/// What it cannot do is change underneath us.
#[cfg(windows)]
fn open_exclusive(path: &Path) -> std::io::Result<std::fs::File> {
    use std::os::windows::fs::OpenOptionsExt;
    use windows_sys::Win32::Storage::FileSystem::FILE_SHARE_READ;

    std::fs::OpenOptions::new()
        .read(true)
        .share_mode(FILE_SHARE_READ)
        .open(path)
}

/// No equivalent sharing model here; the module compiles for the Linux CI runner
/// and its tests, not for a machine that has MusicBee on it.
#[cfg(not(windows))]
fn open_exclusive(path: &Path) -> std::io::Result<std::fs::File> {
    std::fs::File::open(path)
}

fn log_verified(manifest: &Manifest, key: &str) {
    tracing::info!(
        version = %manifest.version,
        key,
        "the staged helper matches the signed manifest"
    );
}

/// `update --pid <us> --staged <dir> --target <dir> --relaunch <exe>`.
fn arguments(staged: &Path, target: &Path, relaunch: &Path) -> Vec<String> {
    vec![
        "update".into(),
        "--pid".into(),
        std::process::id().to_string(),
        "--staged".into(),
        staged.display().to_string(),
        "--target".into(),
        target.display().to_string(),
        "--relaunch".into(),
        relaunch.display().to_string(),
    ]
}

/// Whether this process can write to `dir`, tested by writing rather than by
/// reading an ACL: the question is what the filesystem will let us do, and
/// UAC virtualization and inherited denies make the ACL a poor predictor.
fn is_writable(dir: &Path) -> bool {
    let probe = dir.join(".mbrc-write-probe");
    match std::fs::write(&probe, b"") {
        Ok(()) => {
            let _ = std::fs::remove_file(&probe);
            true
        }
        Err(_) => false,
    }
}

/// The directory this DLL was loaded from, which is MusicBee's plugins
/// directory. Asked of the loader rather than assembled from a MusicBee path:
/// this is the one answer that is true by construction.
#[cfg(windows)]
fn plugins_dir() -> Option<PathBuf> {
    use std::os::windows::ffi::OsStringExt;
    use windows_sys::Win32::Foundation::HMODULE;
    use windows_sys::Win32::System::LibraryLoader::{
        GetModuleFileNameW, GetModuleHandleExW, GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
        GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
    };

    let mut module: HMODULE = std::ptr::null_mut();
    // SAFETY: the address is of a function in this DLL, which is what the
    // FROM_ADDRESS flag expects; UNCHANGED_REFCOUNT means no handle to release.
    let ok = unsafe {
        GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            plugins_dir as *const u16,
            &mut module,
        )
    };
    if ok == 0 {
        return None;
    }

    let mut buffer = [0u16; 32_768];
    // SAFETY: `module` is a live module handle and the buffer length is its real
    // length in units.
    let len = unsafe { GetModuleFileNameW(module, buffer.as_mut_ptr(), buffer.len() as u32) };
    if len == 0 || len as usize >= buffer.len() {
        return None;
    }

    let path = PathBuf::from(std::ffi::OsString::from_wide(&buffer[..len as usize]));
    path.parent().map(Path::to_path_buf)
}

#[cfg(not(windows))]
fn plugins_dir() -> Option<PathBuf> {
    std::env::current_exe()
        .ok()
        .and_then(|exe| exe.parent().map(Path::to_path_buf))
}

/// Starts the helper, with an elevation prompt when `elevate` is set.
///
/// `runas` is what produces the UAC prompt. A declined prompt comes back as
/// `ERROR_CANCELLED`, which is a distinct outcome rather than a failure: the
/// staged download is untouched and the user can press the button again.
#[cfg(windows)]
fn spawn(exe: &Path, arguments: &[String], elevate: bool) -> UpdateLaunch {
    use windows_sys::Win32::Foundation::{GetLastError, ERROR_CANCELLED};
    use windows_sys::Win32::UI::Shell::{ShellExecuteExW, SEE_MASK_NOASYNC, SHELLEXECUTEINFOW};
    use windows_sys::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL;

    let verb = wide(if elevate { "runas" } else { "open" });
    let file = wide(&exe.display().to_string());
    let parameters = wide(&quote(arguments));

    let mut info: SHELLEXECUTEINFOW = unsafe { std::mem::zeroed() };
    info.cbSize = size_of::<SHELLEXECUTEINFOW>() as u32;
    // NOASYNC because this process is about to be asked to exit: the shell must
    // finish launching before we can go away.
    info.fMask = SEE_MASK_NOASYNC;
    info.lpVerb = verb.as_ptr();
    info.lpFile = file.as_ptr();
    info.lpParameters = parameters.as_ptr();
    info.nShow = SW_SHOWNORMAL;

    // SAFETY: every pointer in `info` is a null-terminated wide string that
    // outlives the call, and `cbSize` is the struct's real size.
    let ok = unsafe { ShellExecuteExW(&mut info) };
    if ok != 0 {
        return UpdateLaunch::Launched;
    }

    // SAFETY: no pointers; reads the calling thread's code.
    match unsafe { GetLastError() } {
        ERROR_CANCELLED => {
            tracing::info!("the user declined the elevation prompt");
            UpdateLaunch::Cancelled
        }
        code => {
            tracing::error!(code, "ShellExecuteExW failed");
            UpdateLaunch::Failed
        }
    }
}

#[cfg(not(windows))]
fn spawn(exe: &Path, arguments: &[String], _elevate: bool) -> UpdateLaunch {
    // No elevation model to speak of here, and no MusicBee either. Kept so the
    // module compiles and its tests run on the Linux CI runner.
    match std::process::Command::new(exe).args(arguments).spawn() {
        Ok(_) => UpdateLaunch::Launched,
        Err(_) => UpdateLaunch::Failed,
    }
}

/// Joins arguments into a command line, quoting each.
///
/// Only the Windows launch path builds a command line - off Windows the
/// arguments go to `Command::args` as a list - so this reads as dead code there
/// while its test still runs, which is the point of keeping it compiled.
#[cfg_attr(not(windows), allow(dead_code))]
///
/// Every argument here is either a literal or a path we derived, so this is not
/// trying to be a general Windows quoting routine - it is making sure a path
/// with a space in it (`C:\Program Files\...`, which is the common case) arrives
/// as one argument.
fn quote(arguments: &[String]) -> String {
    arguments
        .iter()
        .map(|a| format!("\"{}\"", a.replace('"', "\\\"")))
        .collect::<Vec<_>>()
        .join(" ")
}

#[cfg(windows)]
fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(std::iter::once(0)).collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn nothing_staged_is_not_a_failure() {
        let dir = std::env::temp_dir().join("mbrc-elevate-empty");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        assert_eq!(
            launch(&dir.to_string_lossy(), "1.5.0.0"),
            UpdateLaunch::NothingStaged,
            "an empty storage directory means there is nothing to apply"
        );
    }

    #[test]
    fn a_staged_bundle_the_release_keys_do_not_trust_is_refused() {
        // The marker says something is staged, the bundle is not signed by a
        // release key, and nothing is launched. This is the check that matters:
        // the file is about to run as administrator.
        let dir = std::env::temp_dir().join("mbrc-elevate-untrusted");
        let _ = std::fs::remove_dir_all(&dir);
        let staged = dir.join(STAGING_DIR).join("9.9.9");
        std::fs::create_dir_all(&staged).unwrap();
        std::fs::write(
            dir.join(STAGING_DIR).join("pending.json"),
            r#"{"schema":1,"version":"9.9.9","staged_at":"2026-08-15T00:00:00Z","files":[]}"#,
        )
        .unwrap();
        std::fs::write(staged.join(MANIFEST_ASSET), "{}").unwrap();
        std::fs::write(staged.join(SIGNATURE_ASSET), "not a signature").unwrap();
        std::fs::write(staged.join(HELPER_EXE), b"not the helper").unwrap();

        assert_eq!(
            launch(&dir.to_string_lossy(), "1.5.0.0"),
            UpdateLaunch::VerifyFailed
        );
    }

    #[test]
    fn arguments_name_this_process_and_the_derived_paths() {
        let args = arguments(
            Path::new("C:/storage/updates/1.6.0"),
            Path::new("C:/Program Files/MusicBee/Plugins"),
            Path::new("C:/Program Files/MusicBee/MusicBee.exe"),
        );
        assert_eq!(args[0], "update");
        assert_eq!(args[2], std::process::id().to_string());
        // The helper parses `--flag value` pairs and rejects anything else, so
        // the shape matters as much as the values.
        assert_eq!(args.len(), 9);
        for flag in ["--pid", "--staged", "--target", "--relaunch"] {
            assert!(args.iter().any(|a| a == flag), "{flag} is missing");
        }
    }

    #[test]
    fn a_path_with_spaces_stays_one_argument() {
        let line = quote(&[
            "--target".into(),
            "C:\\Program Files\\MusicBee\\Plugins".into(),
        ]);
        assert_eq!(
            line,
            "\"--target\" \"C:\\Program Files\\MusicBee\\Plugins\""
        );
    }

    #[test]
    fn only_a_newer_bundle_is_an_upgrade() {
        // The host reports a four-component .NET version; the staged manifest
        // carries three. Both normalize before they are compared.
        assert!(is_upgrade("1.6.0", "1.5.0.0"));
        assert!(is_upgrade("1.5.1", "1.5.0.0"));
        // The case this gate exists for: a real, signed, older release staged by
        // someone who could write to the staging directory.
        assert!(!is_upgrade("1.4.1", "1.5.0.0"));
        assert!(!is_upgrade("1.5.0", "1.5.0.0"));
        // A prerelease sits below the release it precedes, so being offered
        // 1.6.0-rc.1 while running 1.6.0 is not an upgrade either.
        assert!(!is_upgrade("1.6.0-rc.1", "1.6.0.0"));
        assert!(is_upgrade("1.6.0-rc.1", "1.5.0.0"));
        // Unparseable on either side cannot be shown to be newer.
        assert!(!is_upgrade("not-a-version", "1.5.0.0"));
        assert!(!is_upgrade("1.6.0", ""));
    }

    #[test]
    fn writability_is_tested_by_writing() {
        let dir = std::env::temp_dir().join("mbrc-elevate-writable");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        assert!(is_writable(&dir));
        // And the probe does not survive the question being asked.
        assert!(std::fs::read_dir(&dir).unwrap().next().is_none());

        assert!(!is_writable(&dir.join("does-not-exist")));
    }
}
