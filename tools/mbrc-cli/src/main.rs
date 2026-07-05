//! `mbrc` - headless CLI over the shared MusicBee Remote crates.
//!
//! Subcommands (A1): `discover`, `inspect`, `send`. The capture/trim/replay/
//! compare pipeline lands in A2 / Milestone B and is stubbed for now.

use std::process::ExitCode;
use std::time::Duration;

mod args;
mod capture;
mod compare;
mod fuzz;
mod inspect;
mod replay;
mod rng;
mod send;
mod serve;
mod trim;

use args::flag_value;

fn main() -> ExitCode {
    let argv: Vec<String> = std::env::args().collect();
    let rest = &argv[argv.len().min(2)..];
    match argv.get(1).map(String::as_str) {
        Some("discover") => cmd_discover(rest),
        Some("inspect") => inspect::run(rest),
        Some("send") => send::run(rest),
        Some("capture") => capture::run(rest),
        Some("serve") => serve::run(rest),
        Some("trim") => trim::run(rest),
        Some("compare") => compare::run(rest),
        Some("replay") => replay::run(rest),
        Some("fuzz") => fuzz::run(rest),
        Some("help" | "--help" | "-h") | None => {
            print_usage();
            ExitCode::SUCCESS
        }
        Some(other) => {
            eprintln!("unknown subcommand: {other}\n");
            print_usage();
            ExitCode::from(2)
        }
    }
}

fn cmd_discover(args: &[String]) -> ExitCode {
    let timeout_ms: u64 = match flag_value(args, "--timeout-ms")
        .as_deref()
        .unwrap_or("3000")
        .parse()
    {
        Ok(n) => n,
        Err(_) => {
            eprintln!("--timeout-ms must be a number");
            return ExitCode::from(2);
        }
    };
    let timeout = Duration::from_millis(timeout_ms.clamp(500, 10_000));
    match mbrc_discovery::discover_blocking(timeout) {
        Ok(found) => {
            if found.is_empty() {
                println!("no instances found");
            }
            for d in found {
                println!("{}\t{}:{}", d.name, d.address, d.port);
            }
            ExitCode::SUCCESS
        }
        Err(e) => {
            eprintln!("discovery failed: {e}");
            ExitCode::FAILURE
        }
    }
}

fn print_usage() {
    eprintln!(
        "mbrc - MusicBee Remote CLI\n\
         \n\
         USAGE:\n\
         \x20 mbrc <command> [options]\n\
         \n\
         COMMANDS:\n\
         \x20 discover [--timeout-ms N]                 find plugin instances on the LAN\n\
         \x20 inspect  <capture.jsonl>                  summarise an mbrc-capture/2 trace\n\
         \x20 send     [--host H] [--port P] [--json C] connect, handshake, send a command\n\
         \x20          [--client-type T] [--protocol V] [--no-broadcast] [--wait-ms N]\n\
         \x20 capture  --output F [--listen A]          headless tee proxy -> mbrc-capture/2\n\
         \x20          [--upstream B] [--seconds N]\n\
         \x20 serve    --golden <file|dir> [--listen A] replay a capture as a mock server\n\
         \x20          [--seconds N]\n\
         \x20 trim     --in <file|dir> [--out <dir>]    trim a capture into golden fixtures\n\
         \x20 compare  <a> <b> [--values]               diff two captures by endpoint\n\
         \x20 replay   --golden <file|dir> [--host H]   drive a golden against a live server,\n\
         \x20          [--port P] [--values]              record responses, diff vs the golden\n\
         \x20 fuzz     [--host H] [--port P] [--seed N]  seeded protocol fuzzer (read-only\n\
         \x20          [--iterations K] [--corpus G]      default; --diff-host for differential)\n"
    );
}
