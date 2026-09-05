//! Handing a staged update to the elevated helper.
//!
//! The panel presses a button; this decides whether elevation is needed, proves
//! the helper it is about to run is the one the release signed, and launches it.
//! What may be passed to the helper, what is verified before it runs, and what
//! counts as an upgrade are pinned by the tests at the foot of this file; why
//! the verification still holds at launch time is on `VerifiedHelper`.
//!
//! The one rule nothing else records: elevation is asked for **now**, not later.
//! `runas` prompts while the user is still looking at the button they pressed. A
//! helper that self-elevated once MusicBee had exited would leave a declined
//! prompt with no UI to report it into.

use std::path::{Path, PathBuf};

use mbrc_release::{Manifest, stage::STAGING_DIR, verify_bundled_file, verify_manifest};

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
/// time: it denies write and delete sharing, so between the hash check and the
/// launch the file cannot be rewritten, renamed or replaced. That holds for both
/// launch mechanisms - the shell ends up in `CreateProcess` too.
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

/// A `STARTUPINFOEXW` in its documented starting state.
///
/// `cb` and the attribute list are assigned by the caller before it is used.
#[cfg(windows)]
fn empty_startup_info() -> windows_sys::Win32::System::Threading::STARTUPINFOEXW {
    // SAFETY: an all-zero STARTUPINFOEXW is the documented starting value.
    unsafe { std::mem::zeroed() }
}

/// A `PROCESS_INFORMATION` for `CreateProcessW` to fill in.
#[cfg(windows)]
fn empty_process_information() -> windows_sys::Win32::System::Threading::PROCESS_INFORMATION {
    // SAFETY: an all-zero PROCESS_INFORMATION is the documented starting value.
    unsafe { std::mem::zeroed() }
}

/// A `SHELLEXECUTEINFOW` in its documented starting state.
///
/// `cbSize` and the verb are assigned by the caller before it is used.
#[cfg(windows)]
fn empty_shell_execute_info() -> windows_sys::Win32::UI::Shell::SHELLEXECUTEINFOW {
    // SAFETY: an all-zero SHELLEXECUTEINFOW is the documented starting value.
    unsafe { std::mem::zeroed() }
}

/// This thread's last Win32 error code.
#[cfg(windows)]
fn last_error() -> u32 {
    // SAFETY: reads this thread's last error code and has no preconditions.
    unsafe { windows_sys::Win32::Foundation::GetLastError() }
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
    let mut args = vec![
        "update".into(),
        "--pid".into(),
        std::process::id().to_string(),
        "--staged".into(),
        staged.display().to_string(),
        "--target".into(),
        target.display().to_string(),
        "--relaunch".into(),
        relaunch.display().to_string(),
    ];
    if let Some(aumid) = current_aumid() {
        args.push("--relaunch-aumid".into());
        args.push(aumid);
    }
    args
}

/// This process's Application User Model ID, or `None` when it has no package
/// identity - the ordinary desktop install, and by far the common case.
///
/// Only a packaged (Store) MusicBee has one, and only a packaged MusicBee needs
/// it: Windows refuses to execute the image under `WindowsApps` by path, so the
/// package has to be activated by identity instead. Asked of Windows rather than
/// taken from the host, per this module's first rule.
#[cfg(windows)]
fn current_aumid() -> Option<String> {
    use windows_sys::Win32::Foundation::{ERROR_INSUFFICIENT_BUFFER, ERROR_SUCCESS};
    use windows_sys::Win32::Storage::Packaging::Appx::GetCurrentApplicationUserModelId;

    let mut length: u32 = 0;
    // SAFETY: a null buffer with a zero length is the documented way to ask for
    // the required size; nothing is written through the pointer.
    let probe = unsafe { GetCurrentApplicationUserModelId(&mut length, std::ptr::null_mut()) };
    // APPMODEL_ERROR_NO_PACKAGE lands here for an unpackaged process. It is the
    // normal answer, not a failure, so it is not logged as one.
    if probe != ERROR_INSUFFICIENT_BUFFER || length == 0 {
        return None;
    }

    let mut buffer = vec![0u16; length as usize];
    // SAFETY: `buffer` has `length` units, which is what the probe asked for.
    let result = unsafe { GetCurrentApplicationUserModelId(&mut length, buffer.as_mut_ptr()) };
    if result != ERROR_SUCCESS {
        tracing::warn!(result, "could not read this process's application id");
        return None;
    }
    // The length includes the terminating null.
    let end = (length as usize).saturating_sub(1).min(buffer.len());
    Some(String::from_utf16_lossy(&buffer[..end]))
}

#[cfg(not(windows))]
fn current_aumid() -> Option<String> {
    None
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
pub(crate) fn plugins_dir() -> Option<PathBuf> {
    use std::os::windows::ffi::OsStringExt;
    use windows_sys::Win32::Foundation::HMODULE;
    use windows_sys::Win32::System::LibraryLoader::{
        GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS, GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        GetModuleFileNameW, GetModuleHandleExW,
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
pub(crate) fn plugins_dir() -> Option<PathBuf> {
    std::env::current_exe()
        .ok()
        .and_then(|exe| exe.parent().map(Path::to_path_buf))
}

/// Starts the helper, with an elevation prompt when `elevate` is set.
///
/// An ordinary child process when no elevation is needed (see [`spawn_direct`]),
/// and the shell's `runas` verb when it is, since `CreateProcess` cannot elevate.
///
/// A declined UAC prompt comes back as `ERROR_CANCELLED`: a distinct outcome
/// rather than a failure, since the staged download is untouched and the button
/// still works. Unreachable on the direct path, which has no prompt.
#[cfg(windows)]
fn spawn(exe: &Path, arguments: &[String], elevate: bool) -> UpdateLaunch {
    if !elevate {
        return spawn_direct(exe, arguments);
    }
    spawn_elevated(exe, arguments)
}

/// Starts the helper as an ordinary child process.
///
/// `CreateProcess` rather than the shell, which is the whole fix for the Store
/// build: an Explorer-started process runs outside MusicBee's package container,
/// so the MSIX-virtualized paths in its argv resolve to nothing. The caller's
/// verified-helper handle stays meaningful throughout: the image loader's
/// `FILE_EXECUTE` counts as read, so the file runs but cannot be swapped.
///
/// # Why a packaged parent needs more than `Command::spawn`
///
/// A `CreateProcess` child of a packaged (MSIX) app does not inherit the package
/// container: `PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY` defaults to
/// `PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_ENABLE_PROCESS_TREE`. The helper then
/// sees the un-redirected `%APPDATA%`, where nothing we staged exists (observed
/// on a real Store install as `os error 3` resolving `--staged`).
///
/// `PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_OVERRIDE` reverses that for the child
/// being created, so the existing argv needs no path translation - preferable to
/// resolving container paths with `GetFinalPathNameByHandleW`, whose
/// `\?\`-prefixed output the helper's `checked_path` rejects as a network path.
#[cfg(windows)]
fn spawn_direct(exe: &Path, arguments: &[String]) -> UpdateLaunch {
    // Only a packaged parent has the problem, and the attribute is only
    // meaningful there. An ordinary install keeps the plain, well-trodden path.
    if current_aumid().is_some() {
        return spawn_in_container(exe, arguments);
    }
    match std::process::Command::new(exe).args(arguments).spawn() {
        Ok(mut child) => {
            std::thread::sleep(SETTLE);
            match child.try_wait() {
                Ok(Some(status)) => {
                    tracing::error!(%status, "the update helper exited immediately");
                    UpdateLaunch::Failed
                }
                Ok(None) => UpdateLaunch::Launched,
                // The helper did start, and refusing an update that is probably
                // running is the worse answer. Say so and go on.
                Err(e) => {
                    tracing::warn!(error = %e, "could not confirm the update helper is still running");
                    UpdateLaunch::Launched
                }
            }
        }
        Err(e) => {
            tracing::error!(error = %e, "could not start the update helper");
            UpdateLaunch::Failed
        }
    }
}

/// How long to watch a just-started helper before believing it.
///
/// `Launched` makes the panel close MusicBee, so reporting it for a helper that
/// has already refused is the worst outcome available: MusicBee shuts, the
/// update does not happen, and no window is left to say so. Every refusal the
/// helper can reach first happens in milliseconds, while a healthy one then
/// waits minutes for MusicBee, so a short pause separates the two cleanly.
#[cfg(windows)]
const SETTLE: std::time::Duration = std::time::Duration::from_millis(750);

/// Starts the helper as a child that stays *inside* this packaged container.
///
/// `std::process::Command` cannot set proc-thread attributes, so this is raw
/// `CreateProcessW` with an attribute list, and three details are load-bearing:
/// the list is sized by a first, deliberately-failing
/// `InitializeProcThreadAttributeList`; `policy` must outlive it, because
/// `UpdateProcThreadAttribute` stores the pointer rather than the value; and
/// `lpCommandLine` must be writable, because `CreateProcessW` may modify it.
#[cfg(windows)]
fn spawn_in_container(exe: &Path, arguments: &[String]) -> UpdateLaunch {
    use windows_sys::Win32::Foundation::CloseHandle;
    use windows_sys::Win32::System::Threading::{
        CreateProcessW, DeleteProcThreadAttributeList, EXTENDED_STARTUPINFO_PRESENT,
        InitializeProcThreadAttributeList, LPPROC_THREAD_ATTRIBUTE_LIST,
        PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY, PROCESS_INFORMATION, STARTUPINFOEXW,
        UpdateProcThreadAttribute,
    };
    use windows_sys::Win32::System::WindowsProgramming::PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_OVERRIDE;

    let mut command_line = wide(&format!("\"{}\" {}", exe.display(), quote(arguments)));

    // First call fails with ERROR_INSUFFICIENT_BUFFER by design and reports the
    // size; one attribute is all this ever sets.
    let mut size: usize = 0;
    // SAFETY: the documented probe - a null list with the size out-parameter.
    unsafe { InitializeProcThreadAttributeList(std::ptr::null_mut(), 1, 0, &mut size) };
    if size == 0 {
        tracing::error!("could not size the process attribute list");
        return UpdateLaunch::Failed;
    }
    let mut buffer = vec![0u8; size];
    let list = buffer.as_mut_ptr() as LPPROC_THREAD_ATTRIBUTE_LIST;
    // SAFETY: `buffer` is `size` bytes, which is what the probe asked for.
    if unsafe { InitializeProcThreadAttributeList(list, 1, 0, &mut size) } == 0 {
        tracing::error!(
            code = last_error(),
            "could not initialize the process attribute list"
        );
        return UpdateLaunch::Failed;
    }

    // Must outlive `list`: the attribute stores this pointer, it does not copy.
    let policy: u32 = PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_OVERRIDE;
    // SAFETY: `list` is initialized above, and `policy` is a DWORD living until
    // after `DeleteProcThreadAttributeList` below.
    let updated = unsafe {
        UpdateProcThreadAttribute(
            list,
            0,
            PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY as usize,
            &policy as *const u32 as *const std::ffi::c_void,
            size_of::<u32>(),
            std::ptr::null_mut(),
            std::ptr::null(),
        )
    };
    if updated == 0 {
        let code = last_error();
        // SAFETY: `list` was initialized.
        unsafe { DeleteProcThreadAttributeList(list) };
        tracing::error!(code, "could not set the desktop-app breakaway policy");
        return UpdateLaunch::Failed;
    }

    let mut startup: STARTUPINFOEXW = empty_startup_info();
    startup.StartupInfo.cb = size_of::<STARTUPINFOEXW>() as u32;
    startup.lpAttributeList = list;
    let mut process: PROCESS_INFORMATION = empty_process_information();

    // SAFETY: `command_line` is a writable null-terminated wide buffer that
    // outlives the call, and the startup info carries its real size plus the
    // attribute list initialized above.
    let started = unsafe {
        CreateProcessW(
            std::ptr::null(),
            command_line.as_mut_ptr(),
            std::ptr::null(),
            std::ptr::null(),
            0,
            EXTENDED_STARTUPINFO_PRESENT,
            std::ptr::null(),
            std::ptr::null(),
            &startup.StartupInfo,
            &mut process,
        )
    };
    let code = last_error();
    // SAFETY: `list` was initialized and is no longer referenced by anything.
    unsafe { DeleteProcThreadAttributeList(list) };

    if started == 0 {
        tracing::error!(
            code,
            "could not start the update helper inside the container"
        );
        return UpdateLaunch::Failed;
    }
    // The helper outlives us by design, so the handles are closed rather than
    // held to process exit.
    // SAFETY: `CreateProcessW` succeeded, so the handle is live until closed
    // below.
    let status = unsafe { settled_exit_code(process.hProcess) };
    // SAFETY: both handles come from a successful CreateProcessW.
    unsafe {
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
    }

    if let Some(status) = status {
        tracing::error!(
            status,
            "the update helper exited immediately; see mbrc-helper.log"
        );
        return UpdateLaunch::Failed;
    }
    tracing::info!(
        pid = process.dwProcessId,
        "started the update helper inside the package container"
    );
    UpdateLaunch::Launched
}

/// Watches a just-started helper for [`SETTLE`] and reports its exit code if it
/// has already finished.
///
/// `None` means "still running, or could not be determined". Only an observed
/// exit is a reason to refuse: `WAIT_FAILED` and a `GetExitCodeProcess` that
/// does not succeed leave us not knowing, and turning not-knowing into a refusal
/// would strand an update that is very likely applying.
///
/// # Safety
/// `process` must be a live process handle that outlives the call.
#[cfg(windows)]
unsafe fn settled_exit_code(process: windows_sys::Win32::Foundation::HANDLE) -> Option<u32> {
    use windows_sys::Win32::Foundation::WAIT_OBJECT_0;
    use windows_sys::Win32::System::Threading::{GetExitCodeProcess, WaitForSingleObject};

    // SAFETY: the caller's contract says the handle is live for this call.
    if unsafe { WaitForSingleObject(process, SETTLE.as_millis() as u32) } != WAIT_OBJECT_0 {
        return None;
    }
    let mut status: u32 = 0;
    // SAFETY: the same handle, plus `status` as a plain out-parameter.
    if unsafe { GetExitCodeProcess(process, &mut status) } == 0 {
        tracing::warn!("the update helper exited but its status could not be read");
        return None;
    }
    Some(status)
}

/// Starts the helper through the shell's `runas` verb, raising the UAC prompt.
///
/// `SEE_MASK_NOASYNC` because this process is about to be asked to exit: the
/// shell must finish launching before we can go away. `SEE_MASK_NOCLOSEPROCESS`
/// so `hProcess` comes back and the launch can be confirmed the way the direct
/// paths confirm theirs, with the handle ours to close.
#[cfg(windows)]
fn spawn_elevated(exe: &Path, arguments: &[String]) -> UpdateLaunch {
    use windows_sys::Win32::Foundation::{CloseHandle, ERROR_CANCELLED};
    use windows_sys::Win32::UI::Shell::{
        SEE_MASK_NOASYNC, SEE_MASK_NOCLOSEPROCESS, SHELLEXECUTEINFOW, ShellExecuteExW,
    };
    use windows_sys::Win32::UI::WindowsAndMessaging::SW_SHOWNORMAL;

    let verb = wide("runas");
    let file = wide(&exe.display().to_string());
    let parameters = wide(&quote(arguments));

    let mut info: SHELLEXECUTEINFOW = empty_shell_execute_info();
    info.cbSize = size_of::<SHELLEXECUTEINFOW>() as u32;
    info.fMask = SEE_MASK_NOASYNC | SEE_MASK_NOCLOSEPROCESS;
    info.lpVerb = verb.as_ptr();
    info.lpFile = file.as_ptr();
    info.lpParameters = parameters.as_ptr();
    info.nShow = SW_SHOWNORMAL;

    // SAFETY: every pointer in `info` is a null-terminated wide string that
    // outlives the call, and `cbSize` is the struct's real size.
    let ok = unsafe { ShellExecuteExW(&mut info) };
    if ok != 0 {
        // Null when the shell reused an existing process: nothing to watch,
        // and nothing to close.
        if !info.hProcess.is_null() {
            // SAFETY: non-null after a successful `ShellExecuteExW` with
            // NOCLOSEPROCESS, and closed only below.
            let status = unsafe { settled_exit_code(info.hProcess) };
            // SAFETY: the handle came from a successful ShellExecuteExW with
            // NOCLOSEPROCESS, so closing it is this function's job.
            unsafe { CloseHandle(info.hProcess) };
            if let Some(status) = status {
                tracing::error!(
                    status,
                    "the elevated update helper exited immediately; see mbrc-helper.log"
                );
                return UpdateLaunch::Failed;
            }
        }
        return UpdateLaunch::Launched;
    }

    match last_error() {
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
        // The helper parses `--flag value` pairs, so the shape matters as much
        // as the values: eleven only when `--relaunch-aumid` is appended.
        assert!(
            args.len() == 9 || args.len() == 11,
            "unexpected argv shape: {args:?}"
        );
        assert_eq!(
            args.len() == 11,
            args.iter().any(|a| a == "--relaunch-aumid"),
            "the extra pair is the AUMID or nothing"
        );
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
        // Four .NET components against three; both normalize first.
        assert!(is_upgrade("1.6.0", "1.5.0.0"));
        assert!(is_upgrade("1.5.1", "1.5.0.0"));
        // The case the gate exists for: a real, signed, older release.
        assert!(!is_upgrade("1.4.1", "1.5.0.0"));
        assert!(!is_upgrade("1.5.0", "1.5.0.0"));
        // A prerelease sits below the release it precedes.
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
