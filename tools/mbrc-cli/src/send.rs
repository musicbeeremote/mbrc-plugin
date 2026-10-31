//! `mbrc send` - connect to a plugin, run the handshake, optionally send one
//! command, and print frames for a short window.
//!
//! Exercises the shared `mbrc-wire` codec end-to-end: framing, the
//! `player`/`protocol`/`ping` handshake automation, and `context` parsing.
//!
//! With `--protocol 6` it drives V6 instead: the `op:"handshake"` request, then
//! one op (default `ping`) once the handshake is acked, printing the typed
//! `kind:"response"` replies.
//!
//! Usage:
//!
//! ```text
//! mbrc send [--host H] [--port P] [--client-type T] [--protocol V]
//!           [--no-broadcast] [--json '<command>'] [--wait-ms N]
//! mbrc send --protocol 6 [--client-id UUID] [--op NAME] [--json '<data>']
//! ```

use std::process::ExitCode;
use std::time::{Duration, Instant};

use mbrc_wire::{ClientHandshake, FrameAccumulator, frame_line, parse_context};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpStream;
use tokio::net::tcp::{OwnedReadHalf, OwnedWriteHalf};

use crate::args::{endpoint, flag_value, has_flag};

pub fn run(args: &[String]) -> ExitCode {
    let Some((host, port)) = endpoint(args) else {
        return ExitCode::from(2);
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
        let client_id = flag_value(args, "--client-id")
            .unwrap_or_else(|| crate::args::run_client_id("mbrc-cli"));
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
    let Some((mut rd, mut wr)) = open(&host, port).await else {
        return ExitCode::FAILURE;
    };

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

    while let Some(n) = read_before(&mut rd, &mut buf, deadline).await {
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
                // The plugin only accepts commands once we answer the `player` echo with
                // `protocol`, so the queued command goes now.
                if ctx == mbrc_wire::CTX_PLAYER
                    && let Some(cmd) = command.take()
                {
                    let _ = wr.write_all(frame_line(&cmd).as_bytes()).await;
                    let _ = wr.flush().await;
                    println!("> {cmd}");
                }
            }
        }
    }
    ExitCode::SUCCESS
}

/// Drives a V6 connection: handshake, one op once it is acked, then the typed
/// responses for the rest of the window. Newline-framed via `mbrc-wire::v6`.
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

    let Some((mut rd, mut wr)) = open(&host, port).await else {
        return ExitCode::FAILURE;
    };

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

    while let Some(n) = read_before(&mut rd, &mut buf, deadline).await {
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

/// Connects to the plugin, or says why it could not.
async fn open(host: &str, port: u16) -> Option<(OwnedReadHalf, OwnedWriteHalf)> {
    match TcpStream::connect((host, port)).await {
        Ok(stream) => {
            stream.set_nodelay(true).ok();
            Some(stream.into_split())
        }
        Err(e) => {
            eprintln!("connect {host}:{port} failed: {e}");
            None
        }
    }
}

/// Reads what arrives before `deadline`.
///
/// `None` once the window has elapsed, the peer has closed, or the read failed.
async fn read_before(rd: &mut OwnedReadHalf, buf: &mut [u8], deadline: Instant) -> Option<usize> {
    let remaining = deadline.saturating_duration_since(Instant::now());
    if remaining.is_zero() {
        return None;
    }
    match tokio::time::timeout(remaining, rd.read(buf)).await {
        Err(_) | Ok(Ok(0)) => None,
        Ok(Ok(n)) => Some(n),
        Ok(Err(e)) => {
            eprintln!("read failed: {e}");
            None
        }
    }
}
