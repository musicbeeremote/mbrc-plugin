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
//! ```
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
mod firewall;

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
/// The subcommand exists but is not implemented in this build.
const EXIT_NOT_IMPLEMENTED: u8 = 4;

const USAGE: &str = "\
mbrc-helper - elevated helper for MusicBee Remote

USAGE:
    mbrc-helper firewall --port <n>
    mbrc-helper update --pid <n> --staged <dir> --target <dir> --relaunch <exe>
    mbrc-helper --version

COMMANDS:
    firewall    Add or update the inbound firewall rule for the listening port.
    update      Apply a staged update. Not implemented yet.

Both commands require administrative rights.";

fn main() -> ExitCode {
    let args: Vec<String> = std::env::args().skip(1).collect();

    match run(&args) {
        Ok(code) => ExitCode::from(code),
        Err(message) => {
            eprintln!("mbrc-helper: {message}");
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
            // Argument shape is validated now so the contract is settled and the
            // caller can be written against it; applying the update is issue
            // #151. Verification, backup, swap and rollback all land there.
            let flags = parse_flags(&args[1..], &["pid", "staged", "target", "relaunch"])?;
            for name in ["pid", "staged", "target", "relaunch"] {
                require(&flags, name)?;
            }
            let pid = require(&flags, "pid")?;
            pid.parse::<u32>()
                .map_err(|_| format!("--pid must be a process id, got {pid:?}"))?;

            eprintln!(
                "mbrc-helper: `update` is not implemented in this build \
                 (manifest schema {} will be re-verified before any file is replaced)",
                mbrc_release::SCHEMA_VERSION
            );
            Ok(EXIT_NOT_IMPLEMENTED)
        }
        other => Err(format!("unknown command {other:?}\n\n{USAGE}")),
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
    fn update_is_accepted_but_not_implemented() {
        let code = run(&argv(
            "update --pid 1234 --staged a --target b --relaunch c",
        ))
        .unwrap();
        assert_eq!(code, EXIT_NOT_IMPLEMENTED);
    }

    #[test]
    fn version_reports_the_product_version() {
        // Stamped from Directory.Build.props by build.rs, so this fails loudly
        // if the wiring breaks rather than reporting the crate's own 0.1.0.
        assert_eq!(run(&argv("--version")).unwrap(), EXIT_OK);
        assert_ne!(env!("MBRC_VERSION"), "0.1.0");
    }
}
