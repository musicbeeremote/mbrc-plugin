//! `mbrc send` - connect to a plugin, run the handshake, optionally send one
//! command, and print frames for a short window.
//!
//! Exercises the shared `mbrc-wire` codec end-to-end: framing, the
//! `player`/`protocol`/`ping` handshake automation, and `context` parsing.
//!
//! With `--protocol 6` it instead drives the **V6** spine: the `op:"handshake"`
//! request, then one op (default `ping`) once the handshake is acked, printing the
//! typed `kind:"response"` replies. This is the CLI verifier for the V6 spine.
//!
//! Usage:
//!   mbrc send [--host H] [--port P] [--client-type T] [--protocol V]
//!             [--no-broadcast] [--json '<command>'] [--wait-ms N]
//!   mbrc send --protocol 6 [--client-id UUID] [--op NAME] [--json '<data>']

use std::process::ExitCode;
use std::time::{Duration, Instant};

use mbrc_wire::{frame_line, parse_context, ClientHandshake, FrameAccumulator};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpStream;

use crate::args::{flag_value, has_flag};

pub fn run(args: &[String]) -> ExitCode {
    let host = flag_value(args, "--host").unwrap_or_else(|| "127.0.0.1".to_string());
    let port: u16 = match flag_value(args, "--port")
        .as_deref()
        .unwrap_or("3000")
        .parse()
    {
        Ok(p) => p,
        Err(_) => {
            eprintln!("--port must be a number");
            return ExitCode::from(2);
        }
    };
    let client_type = flag_value(args, "--client-type").unwrap_or_else(|| "Android".to_string());
    let protocol: u8 = match flag_value(args, "--protocol")
        .as_deref()
        .unwrap_or("4")
        .parse()
    {
        Ok(p) => p,
        Err(_) => {
            eprintln!("--protocol must be a number");
            return ExitCode::from(2);
        }
    };
    let no_broadcast = has_flag(args, "--no-broadcast");
    let command = flag_value(args, "--json");
    let wait_ms: u64 = match flag_value(args, "--wait-ms")
        .as_deref()
        .unwrap_or("1500")
        .parse()
    {
        Ok(n) => n,
        Err(_) => {
            eprintln!("--wait-ms must be a number");
            return ExitCode::from(2);
        }
    };

    let rt = match tokio::runtime::Runtime::new() {
        Ok(rt) => rt,
        Err(e) => {
            eprintln!("runtime init failed: {e}");
            return ExitCode::FAILURE;
        }
    };

    if protocol == mbrc_wire::v6::PROTOCOL_VERSION as u8 {
        // V6 op data comes from --json (default: an empty object); the op name from
        // --op (default `ping`); a fixed dev client_id unless overridden (the CLI is
        // a test driver, not a persisted install).
        let client_id =
            flag_value(args, "--client-id").unwrap_or_else(|| "mbrc-cli-dev".to_string());
        let op = flag_value(args, "--op").unwrap_or_else(|| "ping".to_string());
        let data = match command.as_deref() {
            None => serde_json::json!({}),
            Some(raw) => match serde_json::from_str(raw) {
                Ok(v) => v,
                Err(e) => {
                    eprintln!("--json is not valid JSON: {e}");
                    return ExitCode::from(2);
                }
            },
        };
        return rt.block_on(run_async_v6(
            host,
            port,
            client_id,
            client_type,
            no_broadcast,
            op,
            data,
            Duration::from_millis(wait_ms),
        ));
    }

    rt.block_on(run_async(
        host,
        port,
        client_type,
        protocol,
        no_broadcast,
        command,
        Duration::from_millis(wait_ms),
    ))
}

#[allow(clippy::too_many_arguments)]
async fn run_async(
    host: String,
    port: u16,
    client_type: String,
    protocol: u8,
    no_broadcast: bool,
    mut command: Option<String>,
    window: Duration,
) -> ExitCode {
    let stream = match TcpStream::connect((host.as_str(), port)).await {
        Ok(s) => s,
        Err(e) => {
            eprintln!("connect {host}:{port} failed: {e}");
            return ExitCode::FAILURE;
        }
    };
    stream.set_nodelay(true).ok();
    let (mut rd, mut wr) = stream.into_split();

    let mut hs = ClientHandshake::new(client_type, protocol, no_broadcast);
    let initial = hs.initial();
    if wr.write_all(frame_line(&initial).as_bytes()).await.is_err() {
        eprintln!("write failed");
        return ExitCode::FAILURE;
    }
    let _ = wr.flush().await;
    println!("> {initial}");

    let mut acc = FrameAccumulator::default();
    let mut buf = vec![0u8; 8192];
    let deadline = Instant::now() + window;

    loop {
        let remaining = deadline.saturating_duration_since(Instant::now());
        if remaining.is_zero() {
            break;
        }
        let n = match tokio::time::timeout(remaining, rd.read(&mut buf)).await {
            Err(_) => break,    // window elapsed
            Ok(Ok(0)) => break, // peer closed
            Ok(Ok(n)) => n,
            Ok(Err(e)) => {
                eprintln!("read failed: {e}");
                break;
            }
        };
        acc.push_bytes(&buf[..n]);
        while let Some(line) = acc.next_frame() {
            if line.trim().is_empty() {
                continue;
            }
            println!("< {line}");
            let ctx = parse_context(&line).unwrap_or_default();
            if let Some(reply) = hs.on_incoming(&ctx) {
                let _ = wr.write_all(frame_line(&reply).as_bytes()).await;
                let _ = wr.flush().await;
                println!("> {reply}");
                // Handshake is complete once we answer the server's `player`
                // echo with `protocol`; the plugin only accepts commands after
                // that, so send the queued command now.
                if ctx == mbrc_wire::CTX_PLAYER {
                    if let Some(cmd) = command.take() {
                        let _ = wr.write_all(frame_line(&cmd).as_bytes()).await;
                        let _ = wr.flush().await;
                        println!("> {cmd}");
                    }
                }
            }
        }
    }
    ExitCode::SUCCESS
}

/// The V6 spine driver: handshake, then one op once the handshake is acked, then
/// print typed responses for the window. Newline-framed via `mbrc-wire::v6`.
#[allow(clippy::too_many_arguments)]
async fn run_async_v6(
    host: String,
    port: u16,
    client_id: String,
    client_type: String,
    no_broadcast: bool,
    op: String,
    data: serde_json::Value,
    window: Duration,
) -> ExitCode {
    use mbrc_wire::v6;

    let stream = match TcpStream::connect((host.as_str(), port)).await {
        Ok(s) => s,
        Err(e) => {
            eprintln!("connect {host}:{port} failed: {e}");
            return ExitCode::FAILURE;
        }
    };
    stream.set_nodelay(true).ok();
    let (mut rd, mut wr) = stream.into_split();

    // client_type maps to the V6 enum (snake_case); default to `cli` for anything
    // unrecognized (the legacy default is "Android").
    let ct = v6::ClientType::parse(&client_type.to_lowercase()).unwrap_or(v6::ClientType::Cli);
    let handshake = v6::handshake_request(&client_id, ct, no_broadcast);
    if wr
        .write_all(v6::frame_line(&handshake).as_bytes())
        .await
        .is_err()
    {
        eprintln!("write failed");
        return ExitCode::FAILURE;
    }
    let _ = wr.flush().await;
    println!("> {handshake}");

    let mut acc = FrameAccumulator::default();
    let mut buf = vec![0u8; 8192];
    let mut op_sent = false;
    let deadline = Instant::now() + window;

    loop {
        let remaining = deadline.saturating_duration_since(Instant::now());
        if remaining.is_zero() {
            break;
        }
        let n = match tokio::time::timeout(remaining, rd.read(&mut buf)).await {
            Err(_) => break,    // window elapsed
            Ok(Ok(0)) => break, // peer closed
            Ok(Ok(n)) => n,
            Ok(Err(e)) => {
                eprintln!("read failed: {e}");
                break;
            }
        };
        acc.push_bytes(&buf[..n]);
        while let Some(line) = acc.next_frame() {
            if line.trim().is_empty() {
                continue;
            }
            println!("< {line}");
            let Some(resp) = v6::parse_response(&line) else {
                continue; // not a response (e.g. an event); just echo above
            };
            // The handshake response carries id 0. On success, fire the op; on
            // failure, report and stop.
            if resp.id == 0 && !op_sent {
                match &resp.result {
                    Ok(_) => {
                        op_sent = true;
                        let req = v6::request(1, &op, data.clone());
                        let _ = wr.write_all(v6::frame_line(&req).as_bytes()).await;
                        let _ = wr.flush().await;
                        println!("> {req}");
                    }
                    Err(e) => {
                        eprintln!("handshake rejected: {} - {}", e.code, e.message);
                        return ExitCode::FAILURE;
                    }
                }
            }
        }
    }
    ExitCode::SUCCESS
}
