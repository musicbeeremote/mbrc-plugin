//! `mbrc replay --golden <file|dir> --host H --port P` - a conformance client.
//!
//! Drives a golden capture's `c2s` frames against a *live* server (the real
//! plugin, `mbrc serve`, or later `mbrc-core`), records the responses into an
//! in-memory capture, and diffs that against the golden. Server-agnostic: the
//! target is just an address, so the same command validates any V4 server.
//!
//! Per connection it replays the handshake (via the shared `ClientHandshake`),
//! blasts the remaining commands, drains the responses, then compares. Draining
//! waits up to ten idle periods while a reply the golden recorded on that
//! connection is still missing, since some host calls outlast one. Volatile
//! state differences (volume, position) are expected against a live plugin - use
//! `--values --ignore playervolume,...` to focus the value diff. Library
//! references in the golden are first bound to live items (see `library_bind`);
//! `--no-bind` sends them as captured.
//!
//! Usage:
//!   mbrc replay --golden <file|dir> [--host H] [--port P] [--wait-ms N]
//!               [--values] [--ignore f1,f2] [--out capture.jsonl]
//!               [--no-bind] [--show-bindings]

use std::collections::BTreeMap;
use std::process::ExitCode;
use std::time::Duration;

use mbrc_capture::{Frame, Record, parse_line};
use mbrc_wire::{CTX_PLAYER, ClientHandshake, FrameAccumulator, frame_line, parse_context};
use tokio::io::{AsyncReadExt, AsyncWrite, AsyncWriteExt};

use crate::args::{endpoint, flag_value, has_flag};
use crate::compare::{diff_report, parse_ignore};
use crate::library_bind::{Binder, LiveLibrary, PROBE_COMMANDS};
use crate::trim::read_all;

/// A connection's replay plan: how to present, and what commands to send.
struct ReplayConn {
    client_type: String,
    protocol: u8,
    commands: Vec<String>,
    /// Contexts of the golden's replies on this connection, one per reply.
    expected_replies: Vec<String>,
}

pub fn run(args: &[String]) -> ExitCode {
    let Some(golden) = flag_value(args, "--golden") else {
        eprintln!(
            "usage: mbrc replay --golden <file|dir> [--host H] [--port P] [--wait-ms N] [--values] [--ignore f1,f2] [--out F] [--no-bind] [--show-bindings]"
        );
        return ExitCode::from(2);
    };
    let Some((host, port)) = endpoint(args) else {
        return ExitCode::from(2);
    };
    let wait = Duration::from_millis(
        flag_value(args, "--wait-ms")
            .and_then(|s| s.parse().ok())
            .unwrap_or(800),
    );
    let values_mode = has_flag(args, "--values");
    let ignore = parse_ignore(args);
    let out_path = flag_value(args, "--out");

    let golden_contents = match read_all(&golden) {
        Ok(c) => c,
        Err(e) => {
            eprintln!("read {golden} failed: {e}");
            return ExitCode::FAILURE;
        }
    };
    let mut plan = plan_connections(&golden_contents);
    if plan.is_empty() {
        eprintln!("no connections found in {golden}");
        return ExitCode::from(2);
    }

    let rt = match tokio::runtime::Runtime::new() {
        Ok(rt) => rt,
        Err(e) => {
            eprintln!("runtime init failed: {e}");
            return ExitCode::FAILURE;
        }
    };
    if !has_flag(args, "--no-bind") {
        let live = match rt.block_on(probe(&host, port, wait)) {
            Ok(live) => live,
            Err(e) => {
                eprintln!("probe of {host}:{port} failed: {e}");
                return ExitCode::FAILURE;
            }
        };
        bind_plan(&mut plan, &live, has_flag(args, "--show-bindings"));
    }
    let replay_contents = match rt.block_on(drive_all(&host, port, &plan, wait)) {
        Ok(c) => c,
        Err(e) => {
            eprintln!("replay against {host}:{port} failed: {e}");
            return ExitCode::FAILURE;
        }
    };

    if let Some(path) = &out_path
        && let Err(e) = std::fs::write(path, &replay_contents)
    {
        eprintln!("write {path} failed: {e}");
    }

    println!(
        "replayed {} connection(s) against {host}:{port}\n",
        plan.len()
    );
    if diff_report(&golden_contents, &replay_contents, values_mode, &ignore) == 0 {
        ExitCode::SUCCESS
    } else {
        ExitCode::FAILURE
    }
}

/// Groups golden `c2s` frames by connection and derive each one's replay plan.
fn plan_connections(golden: &str) -> Vec<ReplayConn> {
    let mut order: Vec<u32> = Vec::new();
    let mut by_conn: BTreeMap<u32, Vec<Frame>> = BTreeMap::new();
    let mut replies: BTreeMap<u32, Vec<String>> = BTreeMap::new();
    for line in golden.lines() {
        let Some(Record::Frame(f)) = parse_line(line) else {
            continue;
        };
        if f.dir == "s2c"
            && f.reply_to.is_some()
            && let Some(ctx) = f.context()
        {
            replies.entry(f.conn_id).or_default().push(ctx.to_string());
        }
        if f.dir == "c2s" {
            by_conn
                .entry(f.conn_id)
                .or_insert_with(|| {
                    order.push(f.conn_id);
                    Vec::new()
                })
                .push(*f);
        }
    }

    order
        .into_iter()
        .map(|cid| {
            let frames = &by_conn[&cid];
            let client_type = frames
                .iter()
                .find(|f| f.context() == Some("player"))
                .and_then(|f| f.frame.as_ref())
                .and_then(|fr| fr.get("data"))
                .and_then(|d| d.as_str())
                .unwrap_or("Android")
                .to_string();
            let protocol = frames
                .iter()
                .find(|f| f.context() == Some("protocol"))
                .and_then(|f| f.frame.as_ref())
                .and_then(|fr| fr.get("data"))
                .and_then(|d| {
                    d.get("protocol_version")
                        .and_then(|v| v.as_u64())
                        .or(d.as_u64())
                })
                .unwrap_or(4) as u8;
            // Everything that isn't handshake/keepalive is a command to replay.
            let commands = frames
                .iter()
                .filter(|f| {
                    !matches!(
                        f.context(),
                        Some("player") | Some("protocol") | Some("pong")
                    )
                })
                .map(|f| f.raw.clone())
                .collect();
            ReplayConn {
                client_type,
                protocol,
                commands,
                expected_replies: replies.remove(&cid).unwrap_or_default(),
            }
        })
        .collect()
}

/// Reads the live library over one Android V4 connection, for binding.
async fn probe(host: &str, port: u16, wait: Duration) -> std::io::Result<LiveLibrary> {
    let conn = ReplayConn {
        client_type: "Android".to_string(),
        protocol: 4,
        commands: PROBE_COMMANDS.iter().map(|c| c.to_string()).collect(),
        expected_replies: PROBE_COMMANDS
            .iter()
            .filter_map(|c| parse_context(c))
            .collect(),
    };
    let mut lines = Vec::new();
    drive_one(host, port, 0, &conn, wait, &mut 0, &mut lines).await?;
    Ok(LiveLibrary::from_probe(&lines))
}

/// Rewrites every planned command's library references, in replay order.
fn bind_plan(plan: &mut [ReplayConn], live: &LiveLibrary, show: bool) {
    let mut binder = Binder::new(live);
    binder.prescan(plan.iter().flat_map(|c| c.commands.iter()));
    let mut rewritten = 0;
    for cmd in plan.iter_mut().flat_map(|c| c.commands.iter_mut()) {
        if let Some(bound) = binder.rewrite(cmd) {
            *cmd = bound;
            rewritten += 1;
        }
    }
    println!(
        "bound {} library reference(s) in {rewritten} command(s) to the live library",
        binder.bindings.len()
    );
    for (kind, golden) in &binder.unbound {
        println!("  unbound {kind:?} {golden:?}: the live library has none");
    }
    if show {
        for (kind, golden, live) in &binder.bindings {
            println!("  {kind:?} {golden:?} -> {live:?}");
        }
    }
}

/// Replays every connection in sequence, accumulating one capture (JSONL string).
async fn drive_all(
    host: &str,
    port: u16,
    plan: &[ReplayConn],
    wait: Duration,
) -> std::io::Result<String> {
    let mut lines: Vec<String> = Vec::new();
    let mut seq: u64 = 0;
    for (i, conn) in plan.iter().enumerate() {
        drive_one(host, port, i as u32, conn, wait, &mut seq, &mut lines).await?;
    }
    Ok(lines.join("\n"))
}

async fn drive_one(
    host: &str,
    port: u16,
    conn_id: u32,
    plan: &ReplayConn,
    wait: Duration,
    seq: &mut u64,
    lines: &mut Vec<String>,
) -> std::io::Result<()> {
    let (mut rd, mut wr) = crate::net::connect(host, port).await?;
    let mut hs = ClientHandshake::new(plan.client_type.clone(), plan.protocol, false);
    let mut acc = FrameAccumulator::default();
    let mut buf = vec![0u8; 8192];
    let mut last_c2s: Option<u64> = None;

    send_line(&mut wr, lines, conn_id, seq, &mut last_c2s, &hs.initial()).await?;

    // Handshake phase: answer the server's `player` echo with `protocol`.
    let mut handshake_done = false;
    while !handshake_done {
        let n = match tokio::time::timeout(wait, rd.read(&mut buf)).await {
            Ok(Ok(0)) | Err(_) => break, // closed or idle before handshake completed
            Ok(Ok(n)) => n,
            Ok(Err(e)) => return Err(e),
        };
        acc.push_bytes(&buf[..n]);
        while let Some(line) = acc.next_frame() {
            if line.trim().is_empty() {
                continue;
            }
            record_s2c(lines, conn_id, seq, last_c2s, &line);
            let ctx = parse_context(&line).unwrap_or_default();
            if let Some(reply) = hs.on_incoming(&ctx) {
                send_line(&mut wr, lines, conn_id, seq, &mut last_c2s, &reply).await?;
                if ctx == CTX_PLAYER {
                    handshake_done = true;
                }
            }
        }
    }

    // Blast the commands.
    for cmd in &plan.commands {
        send_line(&mut wr, lines, conn_id, seq, &mut last_c2s, cmd).await?;
    }

    let mut pending = plan.expected_replies.clone();
    loop {
        let idle = if pending.is_empty() { wait } else { wait * 10 };
        let n = match tokio::time::timeout(idle, rd.read(&mut buf)).await {
            Ok(Ok(0)) | Err(_) => break,
            Ok(Ok(n)) => n,
            Ok(Err(e)) => return Err(e),
        };
        acc.push_bytes(&buf[..n]);
        while let Some(line) = acc.next_frame() {
            if line.trim().is_empty() {
                continue;
            }
            record_s2c(lines, conn_id, seq, last_c2s, &line);
            let ctx = parse_context(&line).unwrap_or_default();
            if let Some(i) = pending.iter().position(|p| *p == ctx) {
                pending.swap_remove(i);
            }
            if let Some(reply) = hs.on_incoming(&ctx) {
                send_line(&mut wr, lines, conn_id, seq, &mut last_c2s, &reply).await?;
            }
        }
    }
    Ok(())
}

async fn send_line<W: AsyncWrite + Unpin>(
    wr: &mut W,
    lines: &mut Vec<String>,
    conn_id: u32,
    seq: &mut u64,
    last_c2s: &mut Option<u64>,
    raw: &str,
) -> std::io::Result<()> {
    wr.write_all(frame_line(raw).as_bytes()).await?;
    wr.flush().await?;
    let s = next(seq);
    *last_c2s = Some(s);
    lines.push(record(conn_id, s, "c2s", None, raw));
    Ok(())
}

fn record_s2c(
    lines: &mut Vec<String>,
    conn_id: u32,
    seq: &mut u64,
    reply_to: Option<u64>,
    raw: &str,
) {
    let s = next(seq);
    lines.push(record(conn_id, s, "s2c", reply_to, raw));
}

fn next(seq: &mut u64) -> u64 {
    let s = *seq;
    *seq += 1;
    s
}

fn record(conn_id: u32, seq: u64, dir: &str, reply_to: Option<u64>, raw: &str) -> String {
    let mut f = Frame::new(conn_id, seq, dir, 0, raw.as_bytes());
    f.reply_to = reply_to;
    serde_json::to_string(&f).unwrap_or_default()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_connection_expects_the_replies_the_golden_recorded_but_not_broadcasts() {
        let golden = [
            record(
                0,
                0,
                "c2s",
                None,
                r#"{"context":"player","data":"Android"}"#,
            ),
            record(
                0,
                1,
                "c2s",
                None,
                r#"{"context":"playlistplay","data":"x.mbp"}"#,
            ),
            record(
                0,
                2,
                "s2c",
                Some(1),
                r#"{"context":"playlistplay","data":true}"#,
            ),
            record(
                0,
                3,
                "s2c",
                None,
                r#"{"context":"playerstate","data":"playing"}"#,
            ),
        ]
        .join("\n");
        let plan = plan_connections(&golden);
        assert_eq!(plan[0].commands.len(), 1);
        assert_eq!(plan[0].expected_replies, vec!["playlistplay"]);
    }
}
