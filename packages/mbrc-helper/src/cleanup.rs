//! Removing the files a plugin uninstall cannot remove itself.
//!
//! When the user removes the plugin from MusicBee's Preferences, MusicBee
//! deletes `mb_remote.dll` - the assembly it loaded - and nothing else. It has
//! never heard of `mbrc_core.dll` or `mbrc-helper.exe`, which sit beside it as
//! ordinary files, so both are left behind. The plugin cannot delete the core
//! itself either: it is mapped into MusicBee for as long as MusicBee is running,
//! and Windows will not unlink a loaded image.
//!
//! So the core copies this helper to a temp directory and starts the copy, which
//! waits for MusicBee to exit and then removes what is left. Unelevated, as the
//! user: if the plugins directory needs administrator rights, the install came
//! from the installer and its uninstaller is the route that removes these files.
//! There is no privilege boundary here and so nothing to verify - unlike the
//! update path, this helper is neither elevated nor acting on a downloaded
//! bundle.

use std::path::{Path, PathBuf};
use std::time::Duration;

use crate::update::{checked_dir, wait_for_exit, Error, Result};

/// How long to wait for MusicBee to exit before giving up.
///
/// Longer than the update path's two minutes, because nothing is pending and the
/// user may well finish the album they are listening to first. Not much longer:
/// a copy of an executable sitting in the temp directory waiting on a process is
/// indistinguishable from something malicious, and the first time this ran on a
/// real machine it was killed on sight - correctly. Fifteen minutes covers
/// "removed the plugin and closed MusicBee", which is the case worth covering,
/// and the cost of missing the rest is two files a reinstall overwrites anyway.
const EXIT_TIMEOUT: Duration = Duration::from_secs(15 * 60);

/// How often to say we are still here while waiting.
///
/// The log is the only thing this process can use to account for itself, and
/// "started, then silence for a quarter of an hour" is what made it look stuck.
const HEARTBEAT: Duration = Duration::from_secs(3 * 60);

/// What the plugin leaves behind, in the order they are removed.
///
/// `mb_remote.dll` is deliberately absent: it is MusicBee's to delete, and its
/// presence is what tells us the removal did not stick (see [`run`]).
const LEFTOVERS: &[&str] = &[
    "mbrc_core.dll",
    // The pre-1.5.0 C# utility this helper replaced. Only present on an install
    // that was upgraded rather than installed fresh.
    "firewall-utility.exe",
    // Last: this is the file the caller copied to run us, or the one we are
    // running from when someone invokes this by hand.
    "mbrc-helper.exe",
];

/// The file whose absence means the plugin really was removed.
const PLUGIN_DLL: &str = "mb_remote.dll";

pub struct Request<'a> {
    pub pid: u32,
    pub target: &'a str,
}

#[derive(Debug)]
pub struct Plan {
    pub pid: u32,
    pub target: PathBuf,
}

/// What happened, for the log and the exit code.
#[derive(Debug, PartialEq, Eq)]
pub enum Outcome {
    /// Files removed. Empty is not reachable here - see `NothingToRemove`.
    Removed(Vec<String>),
    /// `mb_remote.dll` is back: the plugin was reinstalled, or MusicBee never
    /// removed it. Either way these files are in use again and are not ours to
    /// delete.
    StillInstalled,
    /// Nothing of ours is in the directory any more.
    NothingToRemove,
    /// MusicBee outlived the wait. Nothing was touched.
    StillRunning,
    /// Some files could not be removed, with the reasons.
    Partial {
        removed: Vec<String>,
        failed: Vec<String>,
    },
}

pub fn plan(request: &Request<'_>) -> Result<Plan> {
    let target = checked_dir(request.target, "--target")?;
    // A directory with no plugin of ours in it is a caller bug, and this process
    // deletes files: it does not act on a directory it cannot recognise.
    if !LEFTOVERS.iter().any(|name| target.join(name).exists()) && !target.join(PLUGIN_DLL).exists()
    {
        return Err(Error::Rejected(format!(
            "{} holds no MusicBee Remote files",
            target.display()
        )));
    }
    Ok(Plan {
        pid: request.pid,
        target,
    })
}

/// Waits for MusicBee to exit, then removes what the uninstall left behind.
pub fn run(plan: &Plan) -> Outcome {
    run_with(plan, wait_for_exit)
}

/// The seam the tests drive: the wait is injected so the removal can be
/// exercised without a process to wait for.
pub fn run_with(plan: &Plan, wait: fn(u32, Duration) -> bool) -> Outcome {
    crate::log::line(&format!(
        "waiting up to {} minutes for MusicBee (pid {}) to exit; nothing can be removed until it does",
        EXIT_TIMEOUT.as_secs() / 60,
        plan.pid
    ));
    if !wait_for_musicbee(plan.pid, wait) {
        return Outcome::StillRunning;
    }
    crate::log::line("MusicBee exited; removing what the uninstall left behind");

    // Re-checked after the wait, not before: the window between the user
    // removing the plugin and MusicBee exiting is exactly when they might add it
    // back, and deleting the core out from under a reinstalled plugin would
    // break an install that was working a moment ago.
    if plan.target.join(PLUGIN_DLL).exists() {
        return Outcome::StillInstalled;
    }

    let mut removed = Vec::new();
    let mut failed = Vec::new();
    for name in LEFTOVERS {
        let path = plan.target.join(name);
        if !path.exists() {
            continue;
        }
        match std::fs::remove_file(&path) {
            Ok(()) => removed.push((*name).to_owned()),
            Err(e) => failed.push(format!("{name}: {e}")),
        }
    }

    match (removed.is_empty(), failed.is_empty()) {
        (true, true) => Outcome::NothingToRemove,
        (_, false) => Outcome::Partial { removed, failed },
        (false, true) => Outcome::Removed(removed),
    }
}

/// Waits for MusicBee, saying so as it goes.
///
/// Split into heartbeat-sized slices rather than one long wait purely so the log
/// shows the process is alive and why. `true` means MusicBee is gone.
fn wait_for_musicbee(pid: u32, wait: fn(u32, Duration) -> bool) -> bool {
    let mut waited = Duration::ZERO;
    while waited < EXIT_TIMEOUT {
        let slice = HEARTBEAT.min(EXIT_TIMEOUT - waited);
        if wait(pid, slice) {
            return true;
        }
        waited += slice;
        crate::log::line(&format!(
            "still waiting for MusicBee (pid {pid}); {} of {} minutes elapsed",
            waited.as_secs() / 60,
            EXIT_TIMEOUT.as_secs() / 60
        ));
    }
    false
}

/// Whether this process is running from `dir`, which decides whether it can
/// remove itself. Only used to say so in the log: the removal is attempted
/// either way, and Windows refusing to unlink a running image is reported like
/// any other failure.
pub fn running_from(dir: &Path) -> bool {
    crate::update::running_from(dir)
}

#[cfg(test)]
mod tests {
    use super::*;

    struct Fixture {
        root: PathBuf,
    }

    impl Fixture {
        fn new(name: &str) -> Self {
            let root = std::env::temp_dir().join(format!("mbrc-cleanup-{name}"));
            let _ = std::fs::remove_dir_all(&root);
            std::fs::create_dir_all(&root).unwrap();
            Self { root }
        }

        fn write(&self, name: &str) {
            std::fs::write(self.root.join(name), b"stand-in").unwrap();
        }

        fn plan(&self) -> Plan {
            Plan {
                pid: 1,
                target: std::fs::canonicalize(&self.root).unwrap(),
            }
        }
    }

    impl Drop for Fixture {
        fn drop(&mut self) {
            let _ = std::fs::remove_dir_all(&self.root);
        }
    }

    fn gone(_pid: u32, _timeout: Duration) -> bool {
        true
    }

    fn still_running(_pid: u32, _timeout: Duration) -> bool {
        false
    }

    #[test]
    fn removes_what_musicbee_leaves_behind() {
        let fixture = Fixture::new("removes");
        fixture.write("mbrc_core.dll");
        fixture.write("mbrc-helper.exe");

        let outcome = run_with(&fixture.plan(), gone);

        assert_eq!(
            outcome,
            Outcome::Removed(vec!["mbrc_core.dll".into(), "mbrc-helper.exe".into()])
        );
        assert!(!fixture.root.join("mbrc_core.dll").exists());
        assert!(!fixture.root.join("mbrc-helper.exe").exists());
    }

    #[test]
    fn removes_the_retired_firewall_utility_too() {
        let fixture = Fixture::new("retired");
        fixture.write("mbrc_core.dll");
        fixture.write("firewall-utility.exe");

        let outcome = run_with(&fixture.plan(), gone);

        assert_eq!(
            outcome,
            Outcome::Removed(vec!["mbrc_core.dll".into(), "firewall-utility.exe".into()])
        );
    }

    #[test]
    fn a_reinstalled_plugin_is_left_alone() {
        let fixture = Fixture::new("reinstalled");
        fixture.write("mbrc_core.dll");
        fixture.write("mbrc-helper.exe");
        // Added back between the removal and MusicBee exiting.
        fixture.write("mb_remote.dll");

        assert_eq!(run_with(&fixture.plan(), gone), Outcome::StillInstalled);
        assert!(fixture.root.join("mbrc_core.dll").exists());
    }

    #[test]
    fn a_musicbee_that_outlives_the_wait_changes_nothing() {
        let fixture = Fixture::new("still-running");
        fixture.write("mbrc_core.dll");

        assert_eq!(
            run_with(&fixture.plan(), still_running),
            Outcome::StillRunning
        );
        assert!(fixture.root.join("mbrc_core.dll").exists());
    }

    #[test]
    fn an_already_clean_directory_is_not_a_failure() {
        let fixture = Fixture::new("clean");
        assert_eq!(run_with(&fixture.plan(), gone), Outcome::NothingToRemove);
    }

    #[test]
    fn a_directory_with_nothing_of_ours_is_refused() {
        let fixture = Fixture::new("not-ours");
        fixture.write("mb_TheaterModePlugin.dll");

        let target = fixture.root.to_string_lossy().to_string();
        let error = plan(&Request {
            pid: 1,
            target: &target,
        })
        .unwrap_err();

        assert!(matches!(error, Error::Rejected(_)), "{error}");
    }

    #[test]
    fn a_directory_holding_only_the_plugin_is_accepted() {
        // The plugin is still there when the core asks for the cleanup - MusicBee
        // removes it as part of the same teardown - so this has to be allowed.
        let fixture = Fixture::new("plugin-only");
        fixture.write("mb_remote.dll");

        let target = fixture.root.to_string_lossy().to_string();
        assert!(plan(&Request {
            pid: 1,
            target: &target,
        })
        .is_ok());
    }
}
