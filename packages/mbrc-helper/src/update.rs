//! Applying a staged update: re-verify, wait, back up, swap, roll back.
//!
//! This runs elevated, with every path in its argv supplied by an unelevated
//! process. That shapes the whole module:
//!
//! - **The signature is the gate on contents.** The staged bundle sits where any
//!   user process can write, so the manifest is re-verified against the
//!   compiled-in keys and every file re-hashed before a byte is copied - the same
//!   check the core did, deliberately not the same *evidence*.
//! - **The bytes that are verified are the bytes that are written.** Files are
//!   read into memory, verified there, and that buffer is what lands in the
//!   plugins directory; re-reading after verifying would leave a swap window.
//! - **Paths are gates on destination.** Canonicalized, absolute, no UNC, no
//!   reparse points, and every filename written must appear in the verified
//!   manifest. The signature says *what* may be written; these say *where*.
//!
//! `--staged` is an argv input despite the rule against that because elevation
//! can run this as a different administrator account, where a derived
//! `%APPDATA%` would point at the wrong profile. It is hardened as above and the
//! signature carries the trust.

use std::fmt;
use std::path::{Path, PathBuf};
use std::time::Duration;

use mbrc_release::{Manifest, TRUSTED_KEYS, TrustedKey, is_bare_filename, verify_manifest_with};

/// Asset names the core stages alongside the payload.
const MANIFEST_ASSET: &str = "manifest.json";
const SIGNATURE_ASSET: &str = "manifest.json.minisig";

/// Where replaced files are kept, under the same storage directory as the
/// staging tree, which the NSIS uninstaller removes wholesale.
const BACKUP_DIR: &str = "backup";

/// How long to wait for MusicBee to exit before giving up. Generous: a library
/// with a large cache can take a while to shut down, and the failure mode for
/// being impatient is replacing a DLL that is still mapped.
const EXIT_TIMEOUT: Duration = Duration::from_secs(120);

/// What the caller asked for, after argv parsing but before path checking.
pub struct Request<'a> {
    pub pid: u32,
    pub staged: &'a str,
    pub target: &'a str,
    pub relaunch: &'a str,
    /// The Application User Model ID, when MusicBee is a packaged (Store)
    /// install. `None` for an ordinary install, relaunched by path.
    pub relaunch_aumid: Option<&'a str>,
}

/// A request whose paths have been checked and resolved.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Plan {
    pub pid: u32,
    pub staged: PathBuf,
    pub target: PathBuf,
    pub relaunch: RelaunchTarget,
}

/// What to start once the update is in place.
///
/// A packaged install cannot be started by path: Windows denies executing the
/// image under `WindowsApps` directly, and the package has to be activated
/// through its Application User Model ID instead. That is why this is a choice
/// rather than a path - handing Explorer the executable of a packaged MusicBee
/// activates nothing while reporting success, which is exactly how an update on
/// the Store build used to end with MusicBee closed and never reopened.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum RelaunchTarget {
    /// An ordinary install: the MusicBee executable.
    Exe(PathBuf),
    /// A packaged install, named by its AUMID (`<family>!<application>`).
    Packaged(String),
}

impl RelaunchTarget {
    /// What Explorer is handed. It accepts either a path or a
    /// `shell:AppsFolder\<AUMID>` moniker, which is what lets one code path
    /// serve both kinds of install.
    pub fn shell_argument(&self) -> String {
        match self {
            Self::Exe(path) => path.display().to_string(),
            Self::Packaged(aumid) => format!(r"shell:AppsFolder\{aumid}"),
        }
    }
}

/// What an apply did, for the log line.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Applied {
    pub version: String,
    pub files: Vec<String>,
    pub backup: Option<PathBuf>,
}

#[derive(Debug)]
pub enum Error {
    /// A path or argument the elevated helper will not act on.
    Rejected(String),
    /// The staged bundle did not verify. Nothing was touched.
    Verify(mbrc_release::UpdateError),
    /// MusicBee was still running when the wait expired. Nothing was touched.
    StillRunning { pid: u32 },
    /// A file operation failed before anything was replaced.
    Failed(String),
    /// The swap failed part way and the previous files were put back.
    RolledBack { cause: String },
    /// The swap failed part way and the restore failed too. The install may be
    /// inconsistent, which is why this is its own outcome and its own exit code:
    /// it is the one case where the user has to be told to reinstall.
    RollbackFailed { cause: String, restore: String },
}

impl fmt::Display for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Rejected(m) => write!(f, "refusing to continue: {m}"),
            Self::Verify(e) => write!(f, "the staged update did not verify: {e}"),
            Self::StillRunning { pid } => write!(
                f,
                "MusicBee (pid {pid}) did not exit within {}s; nothing was changed",
                EXIT_TIMEOUT.as_secs()
            ),
            Self::Failed(m) => write!(f, "{m}"),
            Self::RolledBack { cause } => {
                write!(
                    f,
                    "update failed ({cause}); the previous files were restored"
                )
            }
            Self::RollbackFailed { cause, restore } => write!(
                f,
                "update failed ({cause}) and the previous files could not be restored \
                 ({restore}); reinstall MusicBee Remote"
            ),
        }
    }
}

type Result<T> = std::result::Result<T, Error>;

/// Checks and resolves the paths in a request.
///
/// Every rejection here is a refusal to act, not a repair: an elevated process
/// given an argv it does not fully understand should stop.
///
/// # Errors
/// A path is missing, relative, a UNC path, or the staged and target
/// directories overlap.
pub fn plan(request: &Request<'_>) -> Result<Plan> {
    let staged = checked_dir(request.staged, "--staged")?;
    let target = checked_dir(request.target, "--target")?;
    // A named package wins: its executable would pass `checked_file` anyway, so
    // only launching it proves anything.
    let relaunch = match request.relaunch_aumid {
        Some(aumid) => RelaunchTarget::Packaged(checked_aumid(aumid)?),
        None => RelaunchTarget::Exe(checked_file(request.relaunch, "--relaunch")?),
    };

    if staged == target {
        return Err(Error::Rejected(
            "--staged and --target are the same directory".into(),
        ));
    }
    // The staged tree is user-writable by design, so an overlap would make the
    // verified bundle and the install the same bytes.
    if target.starts_with(&staged) || staged.starts_with(&target) {
        return Err(Error::Rejected(
            "--staged and --target must not contain one another".into(),
        ));
    }

    Ok(Plan {
        pid: request.pid,
        staged,
        target,
        relaunch,
    })
}

/// Re-verifies the staged bundle, waits for MusicBee to exit, and swaps the
/// files, restoring the previous ones if anything goes wrong part way.
///
/// # Errors
/// As [`apply_with`].
pub fn apply(plan: &Plan) -> Result<Applied> {
    apply_with(plan, TRUSTED_KEYS, wait_for_exit)
}

/// The seam the tests drive: the trust list and the wait are injected so an
/// apply can be exercised end to end without the release keys and without a
/// process to wait for. Everything else is the production path.
///
/// # Errors
/// MusicBee is still running, the staged bundle does not verify, or a file
/// could not be replaced - in which case the previous files are restored first
/// and the restore outcome is part of the error.
pub fn apply_with(
    plan: &Plan,
    keys: &'static [TrustedKey],
    wait: fn(u32, Duration) -> bool,
) -> Result<Applied> {
    let bundle = verify_staged(&plan.staged, keys)?;

    // After verification, before anything is touched: a mapped DLL cannot be
    // replaced, and discovering that mid-swap is what the rollback is for.
    if !wait(plan.pid, EXIT_TIMEOUT) {
        return Err(Error::StillRunning { pid: plan.pid });
    }

    let backup = back_up(plan, &bundle)?;
    match write_all(plan, &bundle) {
        Ok(()) => {
            prune_backups(plan, &bundle.manifest.version);
            Ok(Applied {
                version: bundle.manifest.version,
                files: bundle.files.iter().map(|(n, _)| n.clone()).collect(),
                backup: backup.dir,
            })
        }
        Err(cause) => Err(restore(&backup, &plan.target, cause)),
    }
}

/// A staged bundle that has verified: the manifest, and the payload bytes as
/// they were hashed.
struct Bundle {
    manifest: Manifest,
    files: Vec<(String, Vec<u8>)>,
}

/// Re-verifies a staged directory: signature over the manifest first, then every
/// file the manifest lists.
///
/// The manifest drives this, not the directory listing. A file sitting in the
/// staged directory that the manifest does not name is never read and never
/// copied, whatever it is called.
fn verify_staged(staged: &Path, keys: &'static [TrustedKey]) -> Result<Bundle> {
    let manifest_bytes = read(&staged.join(MANIFEST_ASSET))?;
    let signature = read(&staged.join(SIGNATURE_ASSET))?;
    let signature = String::from_utf8(signature)
        .map_err(|e| Error::Verify(mbrc_release::UpdateError::MalformedSignature(e.to_string())))?;

    let (manifest, key_name) =
        verify_manifest_with(&manifest_bytes, &signature, keys).map_err(Error::Verify)?;
    crate::log::line(&format!(
        "manifest for {} signed by {key_name}",
        manifest.version
    ));

    let mut files = Vec::with_capacity(manifest.files.len());
    for entry in &manifest.files {
        // `Manifest::parse` already enforces this; re-checked because this is the
        // step that turns a manifest string into a path in an elevated process.
        if !is_bare_filename(&entry.path) {
            return Err(Error::Rejected(format!(
                "the manifest names {:?}, which is not a bare filename",
                entry.path
            )));
        }
        let path = staged.join(&entry.path);
        refuse_reparse_point(&path)?;
        let bytes = read(&path)?;
        mbrc_release::verify_sha512(&bytes, &entry.sha512, &entry.path).map_err(Error::Verify)?;
        files.push((entry.path.clone(), bytes));
    }

    Ok(Bundle { manifest, files })
}

/// The files this apply is replacing: the bytes, and where a copy of them was
/// also written for a manual recovery.
struct Backup {
    /// `backup/<version>/`, or `None` when there was nothing to replace.
    dir: Option<PathBuf>,
    /// What was installed before, held in memory. **The restore writes from
    /// here, not from `dir`.** That directory lives under the user's profile,
    /// so any unelevated process can rewrite it between the backup and the
    /// restore; copying it back would turn that write into an elevated one -
    /// the same reason the payload is verified in memory rather than re-read.
    files: Vec<(String, Vec<u8>)>,
}

/// Reads the files about to be replaced, and keeps a copy in `backup/<version>/`.
///
/// Named for the version being applied - "what installing 1.6.0 replaced" -
/// because the version being replaced is not recorded anywhere the helper can
/// read. An empty result is a fresh install rather than an error.
fn back_up(plan: &Plan, bundle: &Bundle) -> Result<Backup> {
    let root = backup_root(&plan.staged)?.join(&bundle.manifest.version);
    if root.exists() {
        std::fs::remove_dir_all(&root).map_err(|e| failed(&root, e))?;
    }

    let mut files = Vec::new();
    for (name, _) in &bundle.files {
        let current = plan.target.join(name);
        if !current.exists() {
            continue;
        }
        refuse_reparse_point(&current)?;
        let bytes = read(&current)?;
        if files.is_empty() {
            std::fs::create_dir_all(&root).map_err(|e| failed(&root, e))?;
        }
        // Written from the bytes just read, so the on-disk copy and the one the
        // restore would use cannot disagree.
        std::fs::write(root.join(name), &bytes).map_err(|e| failed(&current, e))?;
        files.push((name.clone(), bytes));
    }

    if files.is_empty() {
        return Ok(Backup { dir: None, files });
    }
    crate::log::line(&format!(
        "backed up {} file(s) to {}",
        files.len(),
        root.display()
    ));
    Ok(Backup {
        dir: Some(root),
        files,
    })
}

/// Writes the verified bytes into the plugins directory.
fn write_all(plan: &Plan, bundle: &Bundle) -> std::result::Result<(), String> {
    for (name, bytes) in &bundle.files {
        let path = plan.target.join(name);
        // A reparse point here would redirect an elevated write out of the
        // plugins directory entirely.
        if path.exists() {
            refuse_reparse_point(&path).map_err(|e| e.to_string())?;
        }
        std::fs::write(&path, bytes).map_err(|e| format!("{}: {e}", path.display()))?;
        crate::log::line(&format!("wrote {}", path.display()));
    }
    Ok(())
}

/// Puts the previous files back after a failed swap.
///
/// Written from the bytes read during the backup, never re-read from the backup
/// directory: that directory is under the user's profile and an unelevated
/// process can rewrite it while this one is running, which would make the
/// restore an elevated write of somebody else's bytes.
fn restore(backup: &Backup, target: &Path, cause: String) -> Error {
    if backup.files.is_empty() {
        // Nothing was replaced, so there is nothing to put back: this was a
        // fresh install that failed part way.
        return Error::RolledBack { cause };
    }

    let mut failures = Vec::new();
    for (name, bytes) in &backup.files {
        let to = target.join(name);
        if refuse_reparse_point(&to).is_err() {
            failures.push(format!("{name}: became a link"));
            continue;
        }
        if let Err(e) = std::fs::write(&to, bytes) {
            if already_restored(bytes, &to) {
                continue;
            }
            failures.push(format!("{name}: {e}"));
        }
    }

    if failures.is_empty() {
        Error::RolledBack { cause }
    } else {
        Error::RollbackFailed {
            cause,
            restore: failures.join("; "),
        }
    }
}

/// Whether the installed file already holds what the backup would put there.
///
/// Compared by content rather than assumed from the write having failed: the
/// point is to distinguish "never replaced" from "replaced and now unrecoverable",
/// and only the bytes say which.
fn already_restored(backup: &[u8], installed: &Path) -> bool {
    matches!(std::fs::read(installed), Ok(current) if current == backup)
}

/// Keeps the newest backup and removes the rest. One is enough for a manual
/// rollback; more is a slow leak in the user's profile.
fn prune_backups(plan: &Plan, keep: &str) {
    let Ok(root) = backup_root(&plan.staged) else {
        return;
    };
    let Ok(entries) = std::fs::read_dir(&root) else {
        return;
    };
    for entry in entries.flatten() {
        if entry.file_name() == std::ffi::OsStr::new(keep) {
            continue;
        }
        let path = entry.path();
        if path.is_dir()
            && let Err(e) = std::fs::remove_dir_all(&path)
        {
            crate::log::line(&format!("could not remove {}: {e}", path.display()));
        }
    }
}

/// Removes the staged bundle once it has been applied.
///
/// Best effort and deliberately not fatal: the update is already installed, and
/// the marker being left behind costs a redundant offer, not a broken install.
/// Windows will not unlink a running image, so when the helper runs from inside
/// the bundle this cannot succeed at all and the core sweeps the directory on
/// its next start instead.
pub fn clear_staged(plan: &Plan) {
    // `<storage>/updates/<version>` -> remove the version directory and the
    // marker beside it, which is what tells the core something is pending.
    if running_from(&plan.staged) {
        crate::log::line(
            "left the staged bundle for the core to sweep: the helper is running from it",
        );
    } else if let Err(e) = std::fs::remove_dir_all(&plan.staged) {
        crate::log::line(&format!("could not remove {}: {e}", plan.staged.display()));
    }
    if let Some(updates) = plan.staged.parent() {
        let marker = updates.join("pending.json");
        if marker.exists()
            && let Err(e) = std::fs::remove_file(&marker)
        {
            crate::log::line(&format!("could not remove {}: {e}", marker.display()));
        }
    }
}

/// Whether this process's own image lives inside `dir`.
///
/// True for every ordinary apply: the core launches
/// `<storage>/updates/<version>/mbrc-helper.exe` deliberately, because the
/// installed helper is one of the files being replaced. Falls back to false when
/// the executable path cannot be resolved - then the delete is attempted and any
/// failure is reported, which is the honest answer when we cannot tell.
fn running_from(dir: &Path) -> bool {
    let Ok(exe) = std::env::current_exe() else {
        return false;
    };
    let Some(parent) = exe.parent() else {
        return false;
    };
    // Compared canonicalized: `dir` arrives verbatim-prefixed (`\\?\...`) and
    // `current_exe` does not, so the raw paths never match.
    match (std::fs::canonicalize(parent), std::fs::canonicalize(dir)) {
        (Ok(a), Ok(b)) => a == b,
        _ => parent == dir,
    }
}

/// `<storage>/backup`, derived from the staged directory
/// (`<storage>/updates/<version>`) rather than taken from argv.
fn backup_root(staged: &Path) -> Result<PathBuf> {
    staged
        .parent()
        .and_then(Path::parent)
        .map(|storage| storage.join(BACKUP_DIR))
        .ok_or_else(|| {
            Error::Rejected(format!(
                "{} is not inside a staging directory",
                staged.display()
            ))
        })
}

/// Resolves a directory argument, refusing everything an elevated process should
/// not follow.
fn checked_dir(raw: &str, flag: &str) -> Result<PathBuf> {
    let path = checked_path(raw, flag)?;
    if !path.is_dir() {
        return Err(Error::Rejected(format!(
            "{flag} {raw:?} is not a directory"
        )));
    }
    Ok(path)
}

/// As [`checked_dir`], for a file argument.
fn checked_file(raw: &str, flag: &str) -> Result<PathBuf> {
    let path = checked_path(raw, flag)?;
    if !path.is_file() {
        return Err(Error::Rejected(format!("{flag} {raw:?} is not a file")));
    }
    Ok(path)
}

/// The shared path rules.
///
/// Checked on the *input* before canonicalizing, because canonicalization is
/// what would hide a relative path or resolve a link the caller chose.
fn checked_path(raw: &str, flag: &str) -> Result<PathBuf> {
    if raw.trim().is_empty() {
        return Err(Error::Rejected(format!("{flag} is empty")));
    }
    let path = Path::new(raw);
    if !path.is_absolute() {
        return Err(Error::Rejected(format!("{flag} {raw:?} is not absolute")));
    }
    // A UNC path points at another machine, and an elevated process following one
    // is reachable by anyone who can answer as that host.
    if raw.starts_with("\\\\") || raw.starts_with("//") {
        return Err(Error::Rejected(format!("{flag} {raw:?} is a network path")));
    }
    refuse_reparse_point(path)?;
    std::fs::canonicalize(path)
        .map_err(|e| Error::Rejected(format!("{flag} {raw:?} cannot be resolved: {e}")))
}

/// Refuses a symlink or, on Windows, a junction. Missing is not a link.
fn refuse_reparse_point(path: &Path) -> Result<()> {
    match std::fs::symlink_metadata(path) {
        Ok(metadata) if metadata.file_type().is_symlink() => Err(Error::Rejected(format!(
            "{} is a link; refusing to follow it",
            path.display()
        ))),
        _ => Ok(()),
    }
}

fn read(path: &Path) -> Result<Vec<u8>> {
    std::fs::read(path).map_err(|e| failed(path, e))
}

fn failed(path: &Path, e: std::io::Error) -> Error {
    Error::Failed(format!("{}: {e}", path.display()))
}

/// Waits for a process to exit. `true` means it is gone.
///
/// A pid that cannot be opened is treated as already gone: the process either
/// exited before we looked or is not ours to wait on, and both mean waiting will
/// never tell us anything.
#[cfg(windows)]
fn wait_for_exit(pid: u32, timeout: Duration) -> bool {
    use windows::Win32::Foundation::{CloseHandle, WAIT_OBJECT_0};
    use windows::Win32::System::Threading::{
        OpenProcess, PROCESS_SYNCHRONIZE, WaitForSingleObject,
    };

    // SAFETY: a pid is a value, not a pointer; the handle is closed on both
    // paths below.
    let handle = match unsafe { OpenProcess(PROCESS_SYNCHRONIZE, false, pid) } {
        Ok(handle) => handle,
        Err(_) => return true,
    };
    // SAFETY: `handle` came from OpenProcess and has not been closed.
    let result = unsafe { WaitForSingleObject(handle, timeout.as_millis() as u32) };
    // SAFETY: as above; closed exactly once.
    let _ = unsafe { CloseHandle(handle) };
    result == WAIT_OBJECT_0
}

#[cfg(not(windows))]
fn wait_for_exit(_pid: u32, _timeout: Duration) -> bool {
    // Nothing to wait for off Windows; the apply path is exercised by tests that
    // inject their own wait.
    true
}

/// Starts MusicBee again after a successful update.
///
/// Launched **through Explorer**, not directly. This process may be elevated, and
/// a child inherits that: starting MusicBee from here would leave it running as
/// administrator for the rest of the session, writing its settings and cache as
/// a different user. Explorer runs at the user's own integrity level, so handing
/// it the target gets MusicBee back at the level it had before.
pub fn relaunch(target: &RelaunchTarget) {
    relaunch_with(target, spawn_via_explorer);
}

/// The launcher is a parameter so a test can assert what would be started
/// without starting it - the same seam [`apply_with`] uses for the process wait.
fn relaunch_with(target: &RelaunchTarget, launch: fn(&str) -> std::io::Result<()>) {
    let argument = target.shell_argument();
    match launch(&argument) {
        Ok(()) => crate::log::line(&format!("relaunched {argument}")),
        // Not fatal: the update is installed either way. Explorer reports
        // success even when it activates nothing, so this catches little.
        Err(e) => crate::log::line(&format!("could not relaunch {argument}: {e}")),
    }
}

#[cfg(windows)]
fn spawn_via_explorer(argument: &str) -> std::io::Result<()> {
    std::process::Command::new("explorer.exe")
        .arg(argument)
        .spawn()
        .map(|_| ())
}

#[cfg(not(windows))]
fn spawn_via_explorer(_argument: &str) -> std::io::Result<()> {
    Ok(())
}

/// Checks an Application User Model ID before it is handed to Explorer.
///
/// The shape is `<package family>!<application>`. Nothing here reaches a shell -
/// `Command::arg` passes it as a single argument - so this is not about
/// injection. It is that an elevated process should not act on an argument whose
/// shape it cannot vouch for, and a value carrying a path separator is not an
/// AUMID at all.
fn checked_aumid(raw: &str) -> Result<String> {
    let aumid = raw.trim();
    if aumid.is_empty() {
        return Err(Error::Rejected("--relaunch-aumid is empty".into()));
    }
    if aumid.contains(['\\', '/', '"', '\n', '\r']) {
        return Err(Error::Rejected(format!(
            "--relaunch-aumid {raw:?} contains a path separator or a quote"
        )));
    }
    let mut parts = aumid.split('!');
    let (family, app) = (
        parts.next().unwrap_or_default(),
        parts.next().unwrap_or_default(),
    );
    if family.is_empty() || app.is_empty() || parts.next().is_some() {
        return Err(Error::Rejected(format!(
            "--relaunch-aumid {raw:?} is not <family>!<application>"
        )));
    }
    Ok(aumid.to_owned())
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The committed test key. The release keys never sign anything a test can
    /// produce, so an end-to-end apply is driven with this trust list, exactly
    /// as `mbrc-release`'s own tests do.
    const TEST_KEYS: &[TrustedKey] = &[TrustedKey {
        name: "test",
        base64: "RWT+ztjSHP1aBowOy75aVsw0jf2Vn6MMbzuTIAPRaN5EWVPjPU9fjwAj",
    }];

    /// The golden fixture: a real signed manifest for 1.5.0. Its file hashes are
    /// over the byte strings in [`PAYLOAD`].
    const MANIFEST: &str = include_str!("../../mbrc-release/tests/fixtures/manifest.json");
    const SIGNATURE: &str = include_str!("../../mbrc-release/tests/fixtures/manifest.json.minisig");
    const PAYLOAD: &[(&str, &[u8])] = &[
        ("mb_remote.dll", b"mb_remote.dll bytes"),
        ("mbrc_core.dll", b"mbrc_core.dll bytes"),
        ("mbrc-helper.exe", b"mbrc-helper.exe bytes"),
    ];

    fn exited(_pid: u32, _timeout: Duration) -> bool {
        true
    }

    fn still_running(_pid: u32, _timeout: Duration) -> bool {
        false
    }

    /// A storage tree shaped like the real one: `<root>/updates/1.5.0` staged,
    /// `<root>/plugins` standing in for the plugins directory.
    struct Fixture {
        root: PathBuf,
        staged: PathBuf,
        target: PathBuf,
    }

    impl Fixture {
        fn new(name: &str) -> Self {
            let root = std::env::temp_dir().join(format!("mbrc-helper-{name}"));
            let _ = std::fs::remove_dir_all(&root);
            let staged = root.join("updates").join("1.5.0");
            let target = root.join("plugins");
            std::fs::create_dir_all(&staged).unwrap();
            std::fs::create_dir_all(&target).unwrap();

            std::fs::write(staged.join(MANIFEST_ASSET), MANIFEST).unwrap();
            std::fs::write(staged.join(SIGNATURE_ASSET), SIGNATURE).unwrap();
            for (name, bytes) in PAYLOAD {
                std::fs::write(staged.join(name), bytes).unwrap();
            }

            Self {
                root,
                staged,
                target,
            }
        }

        /// Pre-existing installed files, so there is something to back up.
        fn with_installed(self, bytes: &[u8]) -> Self {
            for (name, _) in PAYLOAD {
                std::fs::write(self.target.join(name), bytes).unwrap();
            }
            self
        }

        fn plan(&self) -> Plan {
            Plan {
                pid: 0,
                staged: self.staged.clone(),
                target: self.target.clone(),
                relaunch: RelaunchTarget::Exe(self.root.join("MusicBee.exe")),
            }
        }

        fn installed(&self, name: &str) -> Vec<u8> {
            std::fs::read(self.target.join(name)).unwrap()
        }
    }

    #[test]
    fn applies_every_manifest_file() {
        let fixture = Fixture::new("apply").with_installed(b"old");
        let applied = apply_with(&fixture.plan(), TEST_KEYS, exited).unwrap();

        assert_eq!(applied.version, "1.5.0");
        assert_eq!(applied.files.len(), 3);
        for (name, bytes) in PAYLOAD {
            assert_eq!(fixture.installed(name), *bytes, "{name} was not replaced");
        }
    }

    #[test]
    fn backs_up_what_it_replaces() {
        let fixture = Fixture::new("backup").with_installed(b"old");
        let applied = apply_with(&fixture.plan(), TEST_KEYS, exited).unwrap();

        let backup = applied.backup.expect("a backup must be taken");
        assert_eq!(backup, fixture.root.join("backup").join("1.5.0"));
        for (name, _) in PAYLOAD {
            assert_eq!(std::fs::read(backup.join(name)).unwrap(), b"old");
        }
    }

    #[test]
    fn a_fresh_install_needs_no_backup() {
        let fixture = Fixture::new("fresh");
        let applied = apply_with(&fixture.plan(), TEST_KEYS, exited).unwrap();
        assert!(applied.backup.is_none());
        assert_eq!(fixture.installed("mb_remote.dll"), b"mb_remote.dll bytes");
    }

    #[test]
    fn keeps_only_the_newest_backup() {
        let fixture = Fixture::new("prune").with_installed(b"old");
        let stale = fixture.root.join("backup").join("1.4.0");
        std::fs::create_dir_all(&stale).unwrap();
        std::fs::write(stale.join("mb_remote.dll"), b"older").unwrap();

        apply_with(&fixture.plan(), TEST_KEYS, exited).unwrap();

        assert!(!stale.exists(), "the older backup should have been pruned");
        assert!(fixture.root.join("backup").join("1.5.0").exists());
    }

    #[test]
    fn a_tampered_payload_stops_before_anything_is_written() {
        let fixture = Fixture::new("tampered").with_installed(b"old");
        std::fs::write(fixture.staged.join("mbrc_core.dll"), b"not what was signed").unwrap();

        let err = apply_with(&fixture.plan(), TEST_KEYS, exited).unwrap_err();
        assert!(matches!(err, Error::Verify(_)), "{err}");
        // Crucially, not even the file that *did* verify: verification finishes
        // before the first write.
        for (name, _) in PAYLOAD {
            assert_eq!(fixture.installed(name), b"old", "{name} was touched");
        }
    }

    #[test]
    fn a_tampered_manifest_is_refused() {
        let fixture = Fixture::new("resigned").with_installed(b"old");
        let doctored = MANIFEST.replace("\"version\": \"1.5.0\"", "\"version\": \"9.9.9\"");
        std::fs::write(fixture.staged.join(MANIFEST_ASSET), doctored).unwrap();

        let err = apply_with(&fixture.plan(), TEST_KEYS, exited).unwrap_err();
        assert!(matches!(err, Error::Verify(_)), "{err}");
    }

    #[test]
    fn the_release_keys_do_not_trust_the_test_fixture() {
        // The production entry point uses TRUSTED_KEYS; this is what stops a
        // test-signed bundle being applied by a shipped helper.
        let fixture = Fixture::new("realkeys");
        let err = apply(&fixture.plan()).unwrap_err();
        assert!(matches!(err, Error::Verify(_)), "{err}");
    }

    #[test]
    fn a_file_the_manifest_does_not_name_is_ignored() {
        let fixture = Fixture::new("extra").with_installed(b"old");
        std::fs::write(fixture.staged.join("evil.dll"), b"payload").unwrap();

        apply_with(&fixture.plan(), TEST_KEYS, exited).unwrap();
        assert!(
            !fixture.target.join("evil.dll").exists(),
            "an unlisted file was copied"
        );
    }

    #[test]
    fn a_running_musicbee_stops_the_apply() {
        let fixture = Fixture::new("running").with_installed(b"old");
        let err = apply_with(&fixture.plan(), TEST_KEYS, still_running).unwrap_err();
        assert!(matches!(err, Error::StillRunning { .. }), "{err}");
        assert_eq!(fixture.installed("mb_remote.dll"), b"old");
    }

    /// Marks a file read-only, which is how the tests make one write fail while
    /// the backup copy that precedes it still succeeds.
    ///
    /// Windows-only, and not because the logic is: a read-only bit does not stop
    /// root, so the same test would pass for the wrong reason in a container
    /// running as root. The rollback paths themselves are platform-independent
    /// and this is the one runner where the trick is reliable.
    #[cfg(windows)]
    fn set_read_only(path: &Path, read_only: bool) {
        let mut permissions = std::fs::metadata(path).unwrap().permissions();
        permissions.set_readonly(read_only);
        std::fs::set_permissions(path, permissions).unwrap();
    }

    #[test]
    #[cfg(windows)]
    fn a_failed_write_restores_the_previous_files() {
        let fixture = Fixture::new("rollback").with_installed(b"old");
        // The last file the manifest lists, so the two before it have already
        // been replaced when the write fails.
        let blocked = fixture.target.join("mbrc-helper.exe");
        set_read_only(&blocked, true);

        let err = apply_with(&fixture.plan(), TEST_KEYS, exited).unwrap_err();
        set_read_only(&blocked, false);

        assert!(matches!(err, Error::RolledBack { .. }), "{err}");
        for (name, _) in PAYLOAD {
            assert_eq!(
                fixture.installed(name),
                b"old",
                "{name} was not rolled back"
            );
        }
    }

    #[test]
    #[cfg(windows)]
    fn a_restore_that_cannot_put_a_replaced_file_back_says_so() {
        let fixture = Fixture::new("rollback-failed").with_installed(b"old");
        // Replaced first, then made unwritable, so a genuinely changed file
        // fails to restore - not the read-only-but-untouched case above.
        let plan = fixture.plan();
        let bundle = verify_staged(&plan.staged, TEST_KEYS).unwrap();
        let backup = back_up(&plan, &bundle).unwrap();
        std::fs::write(fixture.target.join("mb_remote.dll"), b"half-written").unwrap();
        let blocked = fixture.target.join("mb_remote.dll");
        set_read_only(&blocked, true);

        let err = restore(&backup, &plan.target, "disk full".into());
        set_read_only(&blocked, false);
        assert!(matches!(err, Error::RollbackFailed { .. }), "{err}");
    }

    #[test]
    fn the_restore_ignores_a_rewritten_backup_directory() {
        // An unelevated process can rewrite the backup mid-apply; restoring the
        // bytes already read is what makes that rewrite inert.
        let fixture = Fixture::new("backup-tampered").with_installed(b"old");
        let plan = fixture.plan();
        let bundle = verify_staged(&plan.staged, TEST_KEYS).unwrap();
        let backup = back_up(&plan, &bundle).unwrap();

        let dir = backup.dir.clone().expect("something was replaced");
        for (name, _) in PAYLOAD {
            std::fs::write(dir.join(name), b"attacker bytes").unwrap();
        }

        let err = restore(&backup, &plan.target, "disk full".into());
        assert!(matches!(err, Error::RolledBack { .. }), "{err}");
        for (name, _) in PAYLOAD {
            assert_eq!(
                fixture.installed(name),
                b"old",
                "{name} was restored from the tampered directory"
            );
        }
    }

    #[test]
    fn clearing_removes_the_bundle_and_the_marker() {
        let fixture = Fixture::new("clear");
        let marker = fixture.staged.parent().unwrap().join("pending.json");
        std::fs::write(&marker, "{}").unwrap();

        clear_staged(&fixture.plan());
        assert!(!fixture.staged.exists());
        assert!(!marker.exists());
    }

    #[test]
    fn relative_and_network_paths_are_refused() {
        let fixture = Fixture::new("paths");
        let staged = fixture.staged.to_string_lossy().into_owned();
        let target = fixture.target.to_string_lossy().into_owned();
        let exe = fixture.root.join("MusicBee.exe");
        std::fs::write(&exe, b"stub").unwrap();
        let relaunch = exe.to_string_lossy().into_owned();

        let cases = [
            ("relative", "plugins"),
            ("network", "\\\\server\\share\\plugins"),
            ("empty", ""),
        ];
        for (label, bad) in cases {
            let request = Request {
                pid: 1,
                staged: &staged,
                target: bad,
                relaunch: &relaunch,
                relaunch_aumid: None,
            };
            assert!(
                matches!(plan(&request), Err(Error::Rejected(_))),
                "{label} path was accepted"
            );
        }

        // The good case, so the rejections above are not passing for some
        // unrelated reason.
        let request = Request {
            pid: 1,
            staged: &staged,
            target: &target,
            relaunch: &relaunch,
            relaunch_aumid: None,
        };
        assert!(plan(&request).is_ok());
    }

    #[test]
    fn overlapping_staged_and_target_are_refused() {
        let fixture = Fixture::new("overlap");
        let exe = fixture.root.join("MusicBee.exe");
        std::fs::write(&exe, b"stub").unwrap();
        let inner = fixture.staged.join("inner");
        std::fs::create_dir_all(&inner).unwrap();

        let request = Request {
            pid: 1,
            staged: &fixture.staged.to_string_lossy(),
            target: &inner.to_string_lossy(),
            relaunch: &exe.to_string_lossy(),
            relaunch_aumid: None,
        };
        assert!(matches!(plan(&request), Err(Error::Rejected(_))));
    }

    // Captures what would have been launched instead of launching it. A `fn`
    // pointer rather than a closure so it matches the seam's signature, which is
    // why the captured value goes through a thread-local.
    thread_local! {
        static LAUNCHED: std::cell::RefCell<Vec<String>> = const { std::cell::RefCell::new(Vec::new()) };
    }

    fn record(argument: &str) -> std::io::Result<()> {
        LAUNCHED.with(|l| l.borrow_mut().push(argument.to_owned()));
        Ok(())
    }

    fn refuse(_argument: &str) -> std::io::Result<()> {
        Err(std::io::Error::other("explorer is not available"))
    }

    #[test]
    fn a_packaged_install_is_relaunched_through_its_aumid() {
        // The whole point of the AUMID: handing Explorer the executable of a
        // packaged MusicBee activates nothing.
        LAUNCHED.with(|l| l.borrow_mut().clear());
        let target =
            RelaunchTarget::Packaged("50072StevenMayall.MusicBee_kcr266et74avj!App".into());

        relaunch_with(&target, record);

        LAUNCHED.with(|l| {
            assert_eq!(
                l.borrow().as_slice(),
                [r"shell:AppsFolder\50072StevenMayall.MusicBee_kcr266et74avj!App"]
            );
        });
    }

    #[test]
    fn an_ordinary_install_is_still_relaunched_by_path() {
        LAUNCHED.with(|l| l.borrow_mut().clear());
        let target = RelaunchTarget::Exe(PathBuf::from(r"C:\Program Files\MusicBee\MusicBee.exe"));

        relaunch_with(&target, record);

        LAUNCHED.with(|l| {
            assert_eq!(
                l.borrow().as_slice(),
                [r"C:\Program Files\MusicBee\MusicBee.exe"]
            );
        });
    }

    #[test]
    fn a_relaunch_that_cannot_start_is_survivable() {
        // The update is already installed by this point; failing to start
        // MusicBee is worth recording, not worth panicking over.
        relaunch_with(&RelaunchTarget::Exe(PathBuf::from(r"C:\nope.exe")), refuse);
    }

    #[test]
    fn an_aumid_that_is_not_one_is_refused() {
        // An elevated process should not act on an argument whose shape it
        // cannot vouch for. A path separator is the giveaway.
        for bad in [
            "",
            "   ",
            "no-bang",
            "!missing-family",
            "missing-app!",
            "two!bangs!here",
            r"family\..\..\evil!App",
            "family/App!App",
            "family\"quote!App",
        ] {
            assert!(
                matches!(checked_aumid(bad), Err(Error::Rejected(_))),
                "{bad:?} was accepted"
            );
        }
    }

    #[test]
    fn a_real_aumid_is_accepted_and_trimmed() {
        let aumid = checked_aumid("  50072StevenMayall.MusicBee_kcr266et74avj!MusicBeePackage  ")
            .expect("a well-formed AUMID");
        assert_eq!(
            aumid,
            "50072StevenMayall.MusicBee_kcr266et74avj!MusicBeePackage"
        );
    }

    #[test]
    fn an_aumid_takes_precedence_over_the_executable() {
        // Both are passed - the executable exists and would validate - and the
        // packaged form has to win, because the path is the one that cannot work.
        let fixture = Fixture::new("aumid-wins");
        let exe = fixture.root.join("MusicBee.exe");
        std::fs::write(&exe, b"stub").unwrap();
        let target = fixture.root.join("plugins");
        std::fs::create_dir_all(&target).unwrap();

        let request = Request {
            pid: 1,
            staged: &fixture.staged.to_string_lossy(),
            target: &target.to_string_lossy(),
            relaunch: &exe.to_string_lossy(),
            relaunch_aumid: Some("Family_abc!App"),
        };
        let plan = plan(&request).expect("plan");
        assert_eq!(
            plan.relaunch,
            RelaunchTarget::Packaged("Family_abc!App".into())
        );
    }

    #[test]
    fn the_staged_bundle_is_left_alone_when_the_helper_runs_from_it() {
        // The real apply always looks like this, so the delete is skipped rather
        // than attempted and reported as a failure.
        let exe = std::env::current_exe().expect("the test binary's own path");
        assert!(running_from(exe.parent().unwrap()));
    }

    #[test]
    fn a_staged_bundle_somewhere_else_is_still_removed() {
        let fixture = Fixture::new("running-from-elsewhere");
        assert!(!running_from(&fixture.staged));
    }

    #[test]
    fn the_backup_root_is_derived_from_the_staged_directory() {
        let staged = Path::new("/storage/updates/1.5.0");
        assert_eq!(
            backup_root(staged).unwrap(),
            Path::new("/storage").join(BACKUP_DIR)
        );
    }
}
