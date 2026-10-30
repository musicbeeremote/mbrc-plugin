//! The listening port's firewall rule, as the setup wizard checks and fixes it.
//!
//! The work happens in `mbrc-helper`, which links the COM firewall API the core
//! does not: `firewall --check` runs directly and only reads, `firewall --port`
//! runs elevated and writes. The core starts it, waits with a deadline, and hands
//! the host a typed answer, so a stuck helper costs a timeout rather than a hang.

use std::io::Read;
use std::path::PathBuf;
use std::process::{Child, Command, ExitStatus, Stdio};
use std::time::{Duration, Instant};

use crate::ffi::dtos::{FirewallFixResult, FirewallStatus};
use crate::updates::elevate::{Elevated, plugins_dir, run_elevated};

/// Long enough for a firewall service that is slow to answer; a check is milliseconds.
const CHECK_TIMEOUT: Duration = Duration::from_secs(10);

/// The prompt has been answered by the time the helper starts, so this covers its work alone.
const FIX_TIMEOUT: Duration = Duration::from_secs(120);

/// The helper's exit code for a run without administrator rights.
const EXIT_ACCESS_DENIED: u32 = 3;

fn helper() -> Option<PathBuf> {
    let path = plugins_dir()?.join("mbrc-helper.exe");
    path.exists().then_some(path)
}

/// Whether our rule lets `port` in; `None` when the helper is missing, fails or overruns.
pub fn check(port: u16) -> Option<FirewallStatus> {
    let exe = helper()?;
    let mut command = Command::new(exe);
    command
        .args(["firewall", "--check", "--port", &port.to_string()])
        .stdout(Stdio::piped())
        .stderr(Stdio::null());
    hide_window(&mut command);
    let mut child = command
        .spawn()
        .map_err(|e| tracing::warn!(error = %e, "could not start the firewall check"))
        .ok()?;

    let status = wait_with_deadline(&mut child, CHECK_TIMEOUT)?;
    if !status.success() {
        tracing::warn!(%status, "the firewall check failed; see mbrc-helper.log");
        return None;
    }
    let mut output = String::new();
    child.stdout.take()?.read_to_string(&mut output).ok()?;
    serde_json::from_str(output.trim())
        .map_err(|e| tracing::warn!(error = %e, output, "the firewall check printed something unexpected"))
        .ok()
}

/// Add or refresh our rule for `port` through the elevated helper, and wait for it.
pub fn fix(port: u16) -> FirewallFixResult {
    let outcome = match helper() {
        None => "failed",
        Some(exe) => {
            let arguments = ["firewall".to_owned(), "--port".to_owned(), port.to_string()];
            match run_elevated(&exe, &arguments, FIX_TIMEOUT) {
                Elevated::Exited(0) => "done",
                Elevated::Exited(EXIT_ACCESS_DENIED) => "not_elevated",
                Elevated::Exited(code) => {
                    tracing::warn!(code, "the firewall fix failed; see mbrc-helper.log");
                    "failed"
                }
                Elevated::Declined => "declined",
                Elevated::TimedOut => "pending",
                Elevated::Failed => "failed",
            }
        }
    };
    FirewallFixResult {
        outcome: outcome.to_owned(),
    }
}

/// The child's exit status, or `None` after killing it at the deadline.
fn wait_with_deadline(child: &mut Child, timeout: Duration) -> Option<ExitStatus> {
    let deadline = Instant::now() + timeout;
    loop {
        match child.try_wait() {
            Ok(Some(status)) => return Some(status),
            Ok(None) if Instant::now() < deadline => std::thread::sleep(Duration::from_millis(50)),
            Ok(None) => {
                tracing::warn!(?timeout, "the firewall check overran; stopping it");
                let _ = child.kill();
                let _ = child.wait();
                return None;
            }
            Err(e) => {
                tracing::warn!(error = %e, "could not wait for the firewall check");
                return None;
            }
        }
    }
}

#[cfg(windows)]
fn hide_window(command: &mut Command) {
    use std::os::windows::process::CommandExt;
    const CREATE_NO_WINDOW: u32 = 0x0800_0000;
    command.creation_flags(CREATE_NO_WINDOW);
}

#[cfg(not(windows))]
fn hide_window(_command: &mut Command) {}

#[cfg(test)]
mod tests {
    use super::*;

    /// What `mbrc-helper firewall --check` prints; its own test serializes to this same file.
    const HELPER_CONTRACT: &str = include_str!(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../tests/golden/firewall-check.json"
    ));

    #[test]
    fn the_helpers_check_output_reads_as_a_status() {
        let status: FirewallStatus = serde_json::from_str(HELPER_CONTRACT.trim()).unwrap();

        assert_eq!(status.rule, "other_profile");
        assert_eq!(status.firewall, "unknown");
        assert_eq!(status.public_network, "on");
    }

    #[test]
    fn a_child_past_its_deadline_is_stopped() {
        let mut child = long_running();
        let started = Instant::now();

        assert!(wait_with_deadline(&mut child, Duration::from_millis(200)).is_none());
        assert!(started.elapsed() < Duration::from_secs(5));
        assert!(
            child.try_wait().unwrap().is_some(),
            "killed, not left running"
        );
    }

    #[cfg(windows)]
    fn long_running() -> Child {
        Command::new("cmd")
            .args(["/C", "ping -n 30 127.0.0.1 > NUL"])
            .spawn()
            .unwrap()
    }

    #[cfg(not(windows))]
    fn long_running() -> Child {
        Command::new("sleep").arg("30").spawn().unwrap()
    }
}
