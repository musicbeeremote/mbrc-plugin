//! Transparent TCP tee proxy for capturing MBRC legacy-protocol traces.
//!
//! Point the Android app at `--listen` instead of the real plugin port;
//! each CRLF-terminated JSON frame flowing in either direction is
//! appended to `--output` as one JSONL record:
//!
//! ```json
//! {"seq":0,"ts":"2026-04-22T10:04:13.124Z","dir":"c2s","frame":{...}}
//! {"seq":1,"ts":"2026-04-22T10:04:13.131Z","dir":"s2c","frame":{...}}
//! ```
//!
//! Frames that don't parse as JSON are still recorded, wrapped as
//! `{"raw": "<text>"}` — the legacy server is strict JSON so this is
//! mostly a safety net for partial reads.

use std::path::PathBuf;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Arc;

use clap::Parser;
use serde::Serialize;
use time::format_description::well_known::Rfc3339;
use time::OffsetDateTime;
use tokio::fs::{File, OpenOptions};
use tokio::io::{AsyncBufReadExt, AsyncRead, AsyncWrite, AsyncWriteExt, BufReader};
use tokio::net::{TcpListener, TcpStream};
use tokio::sync::Mutex;

/// Transparent TCP tee proxy that records CRLF-JSON frames to a JSONL
/// file. Designed for capturing golden-trace fixtures between the MBRC
/// Android app and the legacy C# plugin.
#[derive(Parser, Debug)]
#[command(version, about)]
struct Args {
    /// Address to listen on. Point the Android app at this.
    #[arg(long, default_value = "0.0.0.0:3100")]
    listen: String,

    /// Upstream address to forward to (the real MusicBee plugin).
    #[arg(long, default_value = "127.0.0.1:3000")]
    upstream: String,

    /// JSONL file to append recorded frames to. Created if missing.
    #[arg(long)]
    output: PathBuf,
}

#[derive(Serialize)]
struct Record<'a> {
    seq: u64,
    ts: String,
    dir: &'a str,
    #[serde(skip_serializing_if = "Option::is_none")]
    frame: Option<serde_json::Value>,
    #[serde(skip_serializing_if = "Option::is_none")]
    raw: Option<&'a str>,
    /// Monotonic millisecond mark so latency between a client command
    /// and the server response can be inspected without parsing `ts`.
    elapsed_ms: u128,
}

#[tokio::main]
async fn main() -> std::io::Result<()> {
    let args = Args::parse();

    let logfile: Arc<Mutex<File>> = Arc::new(Mutex::new(
        OpenOptions::new()
            .create(true)
            .append(true)
            .open(&args.output)
            .await?,
    ));
    let seq = Arc::new(AtomicU64::new(0));
    let started = std::time::Instant::now();

    let listener = TcpListener::bind(&args.listen).await?;
    eprintln!(
        "tee-proxy listening on {}, upstream {}, writing {}",
        args.listen,
        args.upstream,
        args.output.display()
    );

    loop {
        tokio::select! {
            // Ctrl-C gracefully closes the listener so the JSONL file
            // is flushed. Accepted connections keep running.
            _ = tokio::signal::ctrl_c() => {
                eprintln!("\nshutting down on Ctrl-C");
                return Ok(());
            }
            accept = listener.accept() => {
                let (client, peer) = match accept {
                    Ok(c) => c,
                    Err(e) => {
                        eprintln!("accept error: {}", e);
                        continue;
                    }
                };
                eprintln!("[{}] client connected", peer);
                let upstream_addr = args.upstream.clone();
                let logfile = Arc::clone(&logfile);
                let seq = Arc::clone(&seq);
                tokio::spawn(async move {
                    if let Err(e) = handle_session(client, &upstream_addr, logfile, seq, started).await {
                        eprintln!("[{}] session ended: {}", peer, e);
                    } else {
                        eprintln!("[{}] session closed", peer);
                    }
                });
            }
        }
    }
}

async fn handle_session(
    client: TcpStream,
    upstream_addr: &str,
    logfile: Arc<Mutex<File>>,
    seq: Arc<AtomicU64>,
    started: std::time::Instant,
) -> std::io::Result<()> {
    let upstream = TcpStream::connect(upstream_addr).await?;

    // No Nagle: latency matters for trace fidelity, and each JSON frame
    // is already its own message.
    client.set_nodelay(true).ok();
    upstream.set_nodelay(true).ok();

    let (cr, cw) = client.into_split();
    let (ur, uw) = upstream.into_split();

    let logfile_c2s = Arc::clone(&logfile);
    let logfile_s2c = Arc::clone(&logfile);
    let seq_c2s = Arc::clone(&seq);
    let seq_s2c = Arc::clone(&seq);

    let c2s = tokio::spawn(async move {
        pipe_frames(BufReader::new(cr), uw, "c2s", logfile_c2s, seq_c2s, started).await
    });
    let s2c = tokio::spawn(async move {
        pipe_frames(BufReader::new(ur), cw, "s2c", logfile_s2c, seq_s2c, started).await
    });

    // Either direction closing ends the session — the other task will
    // observe EOF next read.
    let _ = tokio::try_join!(flatten(c2s), flatten(s2c));
    Ok(())
}

async fn flatten(h: tokio::task::JoinHandle<std::io::Result<()>>) -> std::io::Result<()> {
    match h.await {
        Ok(res) => res,
        Err(e) => Err(std::io::Error::other(e)),
    }
}

async fn pipe_frames<R, W>(
    mut src: BufReader<R>,
    mut dst: W,
    dir: &'static str,
    logfile: Arc<Mutex<File>>,
    seq: Arc<AtomicU64>,
    started: std::time::Instant,
) -> std::io::Result<()>
where
    R: AsyncRead + Unpin,
    W: AsyncWrite + Unpin,
{
    let mut buf = Vec::with_capacity(4096);
    loop {
        buf.clear();
        let n = src.read_until(b'\n', &mut buf).await?;
        if n == 0 {
            // EOF — half-close the destination so the peer sees it too.
            dst.shutdown().await.ok();
            return Ok(());
        }

        // Forward the exact bytes first so any failure to log doesn't
        // stall the wire.
        dst.write_all(&buf).await?;

        // Strip trailing CRLF (or bare LF) for the logged frame; keep
        // the raw bytes on the wire.
        let frame = trim_line_terminator(&buf);
        if frame.is_empty() {
            continue;
        }
        if let Err(e) = write_record(&logfile, &seq, dir, frame, started).await {
            eprintln!("log write failed: {}", e);
        }
    }
}

fn trim_line_terminator(buf: &[u8]) -> &[u8] {
    let mut end = buf.len();
    while end > 0 && matches!(buf[end - 1], b'\n' | b'\r') {
        end -= 1;
    }
    &buf[..end]
}

async fn write_record(
    logfile: &Arc<Mutex<File>>,
    seq: &Arc<AtomicU64>,
    dir: &str,
    frame_bytes: &[u8],
    started: std::time::Instant,
) -> std::io::Result<()> {
    let now = OffsetDateTime::now_utc()
        .format(&Rfc3339)
        .unwrap_or_else(|_| "1970-01-01T00:00:00Z".to_owned());
    let elapsed_ms = started.elapsed().as_millis();
    let seq_val = seq.fetch_add(1, Ordering::Relaxed);

    let text = std::str::from_utf8(frame_bytes).ok();
    let parsed: Option<serde_json::Value> = text.and_then(|t| serde_json::from_str(t).ok());

    let record = Record {
        seq: seq_val,
        ts: now,
        dir,
        frame: parsed,
        raw: if parsed_none(&text) { text } else { None },
        elapsed_ms,
    };

    let mut line = serde_json::to_vec(&record).map_err(std::io::Error::other)?;
    line.push(b'\n');

    let mut f = logfile.lock().await;
    f.write_all(&line).await
}

/// True when we should emit the frame as `raw` because it either
/// wasn't UTF-8 or wasn't valid JSON. Separated so the two cases are
/// obvious at the call site.
fn parsed_none(text: &Option<&str>) -> bool {
    match text {
        None => true,
        Some(t) => serde_json::from_str::<serde_json::Value>(t).is_err(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn trims_crlf() {
        assert_eq!(trim_line_terminator(b"{}\r\n"), b"{}");
    }

    #[test]
    fn trims_bare_lf() {
        assert_eq!(trim_line_terminator(b"{}\n"), b"{}");
    }

    #[test]
    fn leaves_unterminated_alone() {
        assert_eq!(trim_line_terminator(b"{}"), b"{}");
    }

    #[test]
    fn empty_stays_empty() {
        assert_eq!(trim_line_terminator(b"\r\n"), b"");
    }
}
