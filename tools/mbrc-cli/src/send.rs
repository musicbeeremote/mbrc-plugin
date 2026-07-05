//! `mbrc send` - connect to a plugin, run the handshake, optionally send one
//! command, and print frames for a short window.
//!
//! Exercises the shared `mbrc-wire` codec end-to-end: framing, the
//! `player`/`protocol`/`ping` handshake automation, and `context` parsing.
//!
//! Usage:
//!   mbrc send [--host H] [--port P] [--client-type T] [--protocol V]
//!             [--no-broadcast] [--json '<command>'] [--wait-ms N]

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
