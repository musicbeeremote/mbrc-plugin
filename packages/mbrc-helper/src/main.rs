//! `mbrc-helper` - the elevated helper for MusicBee Remote.
//!
//! Two operations need administrative rights and so cannot live in the plugin
//! itself: adding the inbound firewall rule for the listening port, and applying
//! a staged update over the DLLs MusicBee holds open for its whole session.
//! Collapsing both into one binary gives one elevation path, one argv surface to
//! harden, and one file to verify in the update manifest.
//!
//! This replaces the C# `firewall-utility.exe`.
//!
//! ```text
//! mbrc-helper firewall --port <n>
//! mbrc-helper update --pid <n> --staged <dir> --target <dir> --relaunch <exe>
//!                    [--relaunch-aumid <aumid>]
//! mbrc-helper cleanup --pid <n> --target <dir> --storage <dir>
//! ```
//!
//! `cleanup` is the exception to "elevated": it runs as the user, and removes
//! the files a plugin uninstall inside MusicBee cannot remove itself.
//!
//! Exit codes are part of the contract with the caller, which reports them to
//! the user. The old utility printed to a console nobody saw and always exited
//! 0, so a declined elevation prompt was indistinguishable from success.

// Off Windows nothing in here is reachable from `main` - the COM implementation
// is `cfg(windows)` and `run_firewall` becomes a stub - so every item reads as
// dead code. The module still compiles and its unit tests still run on Linux,
// which is the point: the create-or-update logic is platform-independent and CI
// exercises it on both runners.
#[cfg_attr(not(windows), allow(dead_code))]
mod cleanup;
mod firewall;
mod log;
mod update;

use std::collections::BTreeMap;
use std::process::ExitCode;

/// The operation succeeded. This includes writing the rule while the firewall
/// is switched off, which is a normal outcome rather than a degraded one.
const EXIT_OK: u8 = 0;
/// The operation ran and failed for a reason the user cannot act on directly.
///
/// Only reachable from the COM path, which is `cfg(windows)`. The constant is
/// kept unconditionally so the exit-code contract is documented in one place
/// rather than split by platform.
#[cfg_attr(not(windows), allow(dead_code))]
const EXIT_FAILED: u8 = 1;
/// The arguments were wrong. Never reachable from the plugin; means a bug.
const EXIT_USAGE: u8 = 2;
/// Not elevated. The caller can retry with an elevation prompt.
///
/// Windows-only for the same reason as [`EXIT_FAILED`].
#[cfg_attr(not(windows), allow(dead_code))]
const EXIT_ACCESS_DENIED: u8 = 3;
/// The subcommand exists but is not implemented in this build. Now only
/// reachable off Windows, where `firewall` has no implementation; kept in the
/// contract because a caller written against exit code 4 should keep meaning it.
#[cfg_attr(windows, allow(dead_code))]
const EXIT_NOT_IMPLEMENTED: u8 = 4;
/// The staged update did not verify. Nothing was changed.
const EXIT_VERIFY_FAILED: u8 = 5;
/// MusicBee was still running when the wait expired. Nothing was changed.
const EXIT_STILL_RUNNING: u8 = 6;
/// The swap failed part way and the previous files were restored. The install is
/// intact; the update did not happen.
const EXIT_ROLLED_BACK: u8 = 7;
/// The swap failed *and* the restore failed. The only outcome that leaves the
/// install possibly inconsistent, so the caller tells the user to reinstall.
const EXIT_ROLLBACK_FAILED: u8 = 8;

const USAGE: &str = "\
mbrc-helper - elevated helper for MusicBee Remote

USAGE:
    mbrc-helper firewall --port <n>
    mbrc-helper update --pid <n> --staged <dir> --target <dir> --relaunch <exe>
                       [--relaunch-aumid <aumid>]
    mbrc-helper cleanup --pid <n> --target <dir> --storage <dir>
    mbrc-helper --version

COMMANDS:
    firewall    Add or update the inbound firewall rule for the listening port.
    update      Re-verify and apply a staged update, then relaunch MusicBee.
    cleanup     After MusicBee exits, remove the plugin files left behind by an
                uninstall done inside MusicBee.

firewall and update require administrative rights; cleanup deliberately does
not - it runs as the user, and a plugins directory that needs elevation belongs
to an install whose own uninstaller removes these files.

EXIT CODES:
    0  success                    5  the staged update did not verify
    1  failed                     6  MusicBee did not exit in time
    2  bad arguments              7  failed part way; previous files restored
    3  not elevated               8  failed part way; restore also failed
    4  not implemented";

fn main() -> ExitCode {
    let args: Vec<String> = std::env::args().skip(1).collect();

    match run(&args) {
        Ok(code) => ExitCode::from(code),
        Err(message) => {
            log::line(&message);
            ExitCode::from(EXIT_USAGE)
        }
    }
}

/// Returns the exit code on success, or a usage message on a malformed argv.
fn run(args: &[String]) -> Result<u8, String> {
    let Some(command) = args.first() else {
        eprintln!("{USAGE}");
        return Ok(EXIT_USAGE);
    };

    match command.as_str() {
        "--version" | "-V" => {
            println!("mbrc-helper {}", env!("MBRC_VERSION"));
            Ok(EXIT_OK)
        }
        "--help" | "-h" | "help" => {
            println!("{USAGE}");
            Ok(EXIT_OK)
        }
        "firewall" => {
            let flags = parse_flags(&args[1..], &["port"])?;
            let port = require(&flags, "port")?;
            let port: u16 = port
                .parse()
                .map_err(|_| format!("--port must be a number in 1..=65535, got {port:?}"))?;
            if port == 0 {
                return Err("--port must be in 1..=65535, got 0".into());
            }
            Ok(run_firewall(port))
        }
        "update" => {
            // `relaunch-aumid` is accepted but not required: only a packaged
            // (Store) MusicBee has one, and an ordinary install is relaunched by
            // path exactly as before.
            let flags = parse_flags(
                &args[1..],
                &["pid", "staged", "target", "relaunch", "relaunch-aumid"],
            )?;
            for name in ["pid", "staged", "target", "relaunch"] {
                require(&flags, name)?;
            }
            let pid = require(&flags, "pid")?;
            let pid: u32 = pid
                .parse()
                .map_err(|_| format!("--pid must be a process id, got {pid:?}"))?;

            Ok(run_update(&update::Request {
                pid,
                staged: require(&flags, "staged")?,
                target: require(&flags, "target")?,
                relaunch: require(&flags, "relaunch")?,
                relaunch_aumid: flags.get("relaunch-aumid").map(String::as_str),
            }))
        }
        "cleanup" => {
            let flags = parse_flags(&args[1..], &["pid", "target", "storage"])?;
            let pid = require(&flags, "pid")?;
            let pid: u32 = pid
                .parse()
                .map_err(|_| format!("--pid must be a process id, got {pid:?}"))?;

            Ok(run_cleanup(&cleanup::Request {
                pid,
                target: require(&flags, "target")?,
                storage: require(&flags, "storage")?,
            }))
        }
        other => Err(format!("unknown command {other:?}\n\n{USAGE}")),
    }
}

/// Applies a staged update, mapping the outcome to the exit-code contract.
///
/// The distinction the caller acts on: everything up to and including
/// [`EXIT_STILL_RUNNING`] left the install untouched and can simply be retried,
/// [`EXIT_ROLLED_BACK`] means the install is intact but the update did not
/// happen, and [`EXIT_ROLLBACK_FAILED`] is the one that needs the user told.
fn run_update(request: &update::Request<'_>) -> u8 {
    // Before `plan()`, so that plan()'s own refusals are recorded - one of them
    // ("--staged cannot be resolved") is how a packaged install fails, and it
    // used to vanish into a console that does not exist.
    log::direct_to_storage(std::path::Path::new(request.staged));
    log::note_environment();
    log::line(&format!(
        "update requested: pid={} staged={} target={}",
        request.pid, request.staged, request.target
    ));

    let plan = match update::plan(request) {
        Ok(plan) => plan,
        Err(e) => {
            log::line(&e.to_string());
            return EXIT_USAGE;
        }
    };

    match update::apply(&plan) {
        Ok(applied) => {
            log::line(&format!(
                "applied {} ({} file(s))",
                applied.version,
                applied.files.len()
            ));
            // Only after the swap succeeded: a staged bundle that is still there
            // is one the user can retry with.
            update::clear_staged(&plan);
            update::relaunch(&plan.relaunch);
            EXIT_OK
        }
        Err(e) => {
            log::line(&e.to_string());
            match e {
                update::Error::Rejected(_) => EXIT_USAGE,
                update::Error::Verify(_) => EXIT_VERIFY_FAILED,
                update::Error::StillRunning { .. } => EXIT_STILL_RUNNING,
                update::Error::Failed(_) => EXIT_FAILED,
                update::Error::RolledBack { .. } => EXIT_ROLLED_BACK,
                update::Error::RollbackFailed { .. } => EXIT_ROLLBACK_FAILED,
            }
        }
    }
}

/// Removes what a plugin uninstall left behind, once MusicBee has exited.
///
/// Every outcome except a MusicBee that outlived the wait is a success: a
/// directory with nothing left in it and a plugin that was added back again are
/// both correct answers to "remove what is left", and neither is worth an error
/// the caller cannot act on - by the time this runs, the caller is gone.
fn run_cleanup(request: &cleanup::Request<'_>) -> u8 {
    // No storage directory to log beside: the plugin deleted it on its way out.
    log::direct_to_temp();
    log::note_environment();
    log::line(&format!(
        "cleanup requested: pid={} target={} storage={}",
        request.pid, request.target, request.storage
    ));

    let plan = match cleanup::plan(request) {
        Ok(plan) => plan,
        Err(e) => {
            log::line(&e.to_string());
            return EXIT_USAGE;
        }
    };
    if cleanup::running_from(&plan.target) {
        // The caller is expected to run a copy from somewhere else, because a
        // running image cannot be unlinked. Said here so the failure below reads
        // as the consequence it is rather than a mystery.
        log::line("running from the directory being cleaned; this build cannot remove itself");
    }

    match cleanup::run(&plan) {
        cleanup::Outcome::Removed(files) => {
            log::line(&format!("removed {}", files.join(", ")));
            EXIT_OK
        }
        cleanup::Outcome::NothingToRemove => {
            log::line("nothing left to remove");
            EXIT_OK
        }
        cleanup::Outcome::StillInstalled => {
            log::line("the plugin is installed again; left everything in place");
            EXIT_OK
        }
        cleanup::Outcome::StillRunning => {
            log::line("MusicBee did not exit in time; left everything in place");
            EXIT_STILL_RUNNING
        }
        cleanup::Outcome::Partial { removed, failed } => {
            if !removed.is_empty() {
                log::line(&format!("removed {}", removed.join(", ")));
            }
            log::line(&format!("could not remove {}", failed.join("; ")));
            EXIT_FAILED
        }
    }
}

#[cfg(windows)]
fn run_firewall(port: u16) -> u8 {
    use firewall::{ensure_rule, RULE_NAME};

    let policy = match firewall::com::ComPolicy::new() {
        Ok(policy) => policy,
        Err(e) => return report(e),
    };

    match ensure_rule(&policy, RULE_NAME, port) {
        Ok(report) => {
            println!("mbrc-helper: port {port}: {report}");
            EXIT_OK
        }
        Err(e) => report(e),
    }
}

#[cfg(not(windows))]
fn run_firewall(_port: u16) -> u8 {
    eprintln!("mbrc-helper: the firewall command is Windows-only");
    EXIT_NOT_IMPLEMENTED
}

/// Prints a failure and maps it to the exit code the caller distinguishes on.
#[cfg(windows)]
fn report(e: firewall::Error) -> u8 {
    eprintln!("mbrc-helper: {e}");
    match e {
        firewall::Error::AccessDenied => EXIT_ACCESS_DENIED,
        firewall::Error::Com(_) => EXIT_FAILED,
    }
}

/// Parses `--name value` pairs, accepting only the flags in `allowed`.
///
/// Strict on purpose. This binary runs elevated, so an argv it does not fully
/// understand is refused rather than partly acted on: no positional arguments,
/// no unknown flags, no repeats, no missing values.
fn parse_flags(args: &[String], allowed: &[&str]) -> Result<BTreeMap<String, String>, String> {
    let mut flags = BTreeMap::new();
    let mut i = 0;

    while i < args.len() {
        let arg = &args[i];
        let Some(name) = arg.strip_prefix("--") else {
            return Err(format!("expected a --flag, got {arg:?}"));
        };
        if !allowed.contains(&name) {
            return Err(format!(
                "unknown flag --{name}; expected one of {}",
                allowed
                    .iter()
                    .map(|a| format!("--{a}"))
                    .collect::<Vec<_>>()
                    .join(", ")
            ));
        }
        let Some(value) = args.get(i + 1) else {
            return Err(format!("--{name} requires a value"));
        };
        // A value that looks like a flag means the real value was omitted.
        if value.starts_with("--") {
            return Err(format!("--{name} requires a value, got {value:?}"));
        }
        if flags.insert(name.to_string(), value.clone()).is_some() {
            return Err(format!("--{name} given more than once"));
        }
        i += 2;
    }

    Ok(flags)
}

fn require<'a>(flags: &'a BTreeMap<String, String>, name: &str) -> Result<&'a String, String> {
    flags
        .get(name)
        .ok_or_else(|| format!("--{name} is required"))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn argv(s: &str) -> Vec<String> {
        s.split_whitespace().map(str::to_string).collect()
    }

    #[test]
    fn parses_a_well_formed_flag_list() {
        let flags = parse_flags(&argv("--port 3000"), &["port"]).unwrap();
        assert_eq!(flags["port"], "3000");
    }

    #[test]
    fn rejects_unknown_flags() {
        let err = parse_flags(&argv("--bogus 1"), &["port"]).unwrap_err();
        assert!(err.contains("unknown flag --bogus"), "{err}");
    }

    #[test]
    fn rejects_positional_arguments() {
        let err = parse_flags(&argv("3000"), &["port"]).unwrap_err();
        assert!(err.contains("expected a --flag"), "{err}");
    }

    #[test]
    fn rejects_repeated_flags() {
        let err = parse_flags(&argv("--port 3000 --port 3001"), &["port"]).unwrap_err();
        assert!(err.contains("more than once"), "{err}");
    }

    #[test]
    fn rejects_a_flag_with_no_value() {
        let err = parse_flags(&argv("--port"), &["port"]).unwrap_err();
        assert!(err.contains("requires a value"), "{err}");
    }

    #[test]
    fn rejects_a_flag_whose_value_is_another_flag() {
        let err = parse_flags(&argv("--pid --staged x"), &["pid", "staged"]).unwrap_err();
        assert!(err.contains("--pid requires a value"), "{err}");
    }

    #[test]
    fn rejects_a_non_numeric_port() {
        let err = run(&argv("firewall --port 3000x")).unwrap_err();
        assert!(err.contains("--port must be a number"), "{err}");
    }

    #[test]
    fn rejects_an_out_of_range_port() {
        // 65536 does not fit u16, and 0 is not a listening port.
        assert!(run(&argv("firewall --port 65536")).is_err());
        assert!(run(&argv("firewall --port 0")).is_err());
    }

    #[test]
    fn rejects_an_unknown_command() {
        let err = run(&argv("frobnicate")).unwrap_err();
        assert!(err.contains("unknown command"), "{err}");
    }

    #[test]
    fn update_requires_its_full_argument_set() {
        let err = run(&argv("update --pid 1234")).unwrap_err();
        assert!(err.contains("--staged is required"), "{err}");
    }

    #[test]
    fn update_refuses_paths_it_cannot_vouch_for() {
        // Well-formed argv, unusable paths: the argv parser is happy and the
        // path checks are what stop it. Nothing is touched, and the exit code
        // says "bad arguments" rather than "failed".
        let code = run(&argv(
            "update --pid 1234 --staged a --target b --relaunch c",
        ))
        .unwrap();
        assert_eq!(code, EXIT_USAGE);
    }

    #[test]
    fn version_reports_the_product_version() {
        // Stamped from Directory.Build.props by build.rs, so this fails loudly
        // if the wiring breaks rather than reporting the crate's own 0.1.0.
        assert_eq!(run(&argv("--version")).unwrap(), EXIT_OK);
        assert_ne!(env!("MBRC_VERSION"), "0.1.0");
    }
}
