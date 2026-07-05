//! `mbrc serve --golden <file|dir>` - replay a captured trace *as a server*.
//!
//! Accepts real clients (a phone app, `mbrc send`, later the Rust core's own
//! client tests) and answers each command with the golden's matching `s2c`
//! frames - no MusicBee required. Responses are keyed by the `context` of the
//! `c2s` frame they replied to (via the capture's `reply_to`), so the handshake
//! (`player`/`protocol`) replays naturally alongside every other command.
//!
//! Usage: mbrc serve --golden <file|dir> [--listen A] [--seconds N]

use std::collections::BTreeMap;
use std::process::ExitCode;
use std::sync::Arc;
use std::time::Duration;

use mbrc_capture::{parse_line, Record};
use mbrc_wire::{frame_line, parse_context, FrameAccumulator};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::{TcpListener, TcpStream};

use crate::args::flag_value;
use crate::trim::read_all;

/// `context` -> distinct `s2c` raw frames observed replying to it.
type Responses = BTreeMap<String, Vec<String>>;

pub fn run(args: &[String]) -> ExitCode {
    let Some(golden) = flag_value(args, "--golden") else {
        eprintln!("usage: mbrc serve --golden <file|dir> [--listen A] [--seconds N]");
        return ExitCode::from(2);
    };
    let listen = flag_value(args, "--listen").unwrap_or_else(|| "0.0.0.0:3000".to_string());
    let seconds = flag_value(args, "--seconds").and_then(|s| s.parse::<u64>().ok());

    let contents = match read_all(&golden) {
        Ok(c) => c,
        Err(e) => {
            eprintln!("read {golden} failed: {e}");
            return ExitCode::FAILURE;
        }
    };
    let responses = build_responses(&contents);
    if responses.is_empty() {
        eprintln!("warning: no correlated responses in {golden} (clients will get silence)");
    }

    let rt = match tokio::runtime::Runtime::new() {
        Ok(rt) => rt,
        Err(e) => {
            eprintln!("runtime init failed: {e}");
            return ExitCode::FAILURE;
        }
    };
    rt.block_on(run_async(listen, Arc::new(responses), seconds))
}

/// Map each `c2s` context to the distinct `s2c` frames that replied to it.
fn build_responses(contents: &str) -> Responses {
    let frames: Vec<mbrc_capture::Frame> = contents
        .lines()
        .filter_map(|l| match parse_line(l) {
            Some(Record::Frame(f)) => Some(*f),
            _ => None,
        })
        .collect();

    let mut seq_ctx: BTreeMap<u64, String> = BTreeMap::new();
    for f in &frames {
        if f.dir == "c2s" {
            if let Some(c) = f.context() {
                seq_ctx.insert(f.seq, c.to_string());
            }
        }
    }

    let mut out: Responses = BTreeMap::new();
    for f in &frames {
        if f.dir == "s2c" {
            if let Some(ctx) = f.reply_to.and_then(|r| seq_ctx.get(&r)) {
                let bucket = out.entry(ctx.clone()).or_default();
                if !bucket.contains(&f.raw) {
                    bucket.push(f.raw.clone());
                }
            }
        }
    }
    out
}

async fn run_async(listen: String, responses: Arc<Responses>, seconds: Option<u64>) -> ExitCode {
    let listener = match TcpListener::bind(&listen).await {
        Ok(l) => l,
        Err(e) => {
            eprintln!("bind {listen} failed: {e}");
            return ExitCode::FAILURE;
        }
    };
    eprintln!("serving {} endpoint(s) on {listen}", responses.len());

    let accept = tokio::spawn(async move {
        loop {
            match listener.accept().await {
                Ok((stream, _)) => {
                    let responses = Arc::clone(&responses);
                    tokio::spawn(async move {
                        let _ = handle_client(stream, responses).await;
                    });
                }
                Err(e) => eprintln!("accept error: {e}"),
            }
        }
    });

    match seconds {
        Some(n) => {
            tokio::time::sleep(Duration::from_secs(n)).await;
            eprintln!("serve window ({n}s) elapsed");
        }
        None => {
            let _ = tokio::signal::ctrl_c().await;
            eprintln!("interrupted");
        }
    }
    accept.abort();
    ExitCode::SUCCESS
}

async fn handle_client(stream: TcpStream, responses: Arc<Responses>) -> std::io::Result<()> {
    stream.set_nodelay(true).ok();
    let (mut rd, mut wr) = stream.into_split();
    let mut acc = FrameAccumulator::default();
    let mut buf = vec![0u8; 8192];

    loop {
        let n = rd.read(&mut buf).await?;
        if n == 0 {
            return Ok(());
        }
        acc.push_bytes(&buf[..n]);
        while let Some(line) = acc.next_frame() {
            if line.trim().is_empty() {
                continue;
            }
            let ctx = parse_context(&line).unwrap_or_default();
            if let Some(replies) = responses.get(&ctx) {
                for r in replies {
                    wr.write_all(frame_line(r).as_bytes()).await?;
                }
                wr.flush().await?;
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn correlates_responses_by_context() {
        // c2s playerstatus (seq 4) -> s2c (reply_to 4).
        let contents = [
            r#"{"type":"frame","conn_id":0,"seq":4,"dir":"c2s","ts":"t","elapsed_ms":0,"raw":"{\"context\":\"playerstatus\"}","frame":{"context":"playerstatus"}}"#,
            r#"{"type":"frame","conn_id":0,"seq":5,"dir":"s2c","reply_to":4,"ts":"t","elapsed_ms":0,"raw":"{\"context\":\"playerstatus\",\"data\":{\"playervolume\":\"81\"}}","frame":{"context":"playerstatus","data":{"playervolume":"81"}}}"#,
        ]
        .join("\n");
        let r = build_responses(&contents);
        let replies = r.get("playerstatus").expect("mapped");
        assert_eq!(replies.len(), 1);
        assert!(replies[0].contains("playervolume"));
    }
}
