//! `mbrc capture` - headless transparent TCP tee proxy that writes an
//! `mbrc-capture/2` trace. The same tee the debugger's Proxy mode runs, minus
//! the UI: point a real client at `--listen`, it forwards to `--upstream`
//! byte-for-byte, and every frame plus the connection lifecycle is appended to
//! `--output`.
//!
//! Usage:
//!   mbrc capture --output <file.jsonl> [--listen A] [--upstream B] [--seconds N]
//!
//! Runs until `--seconds` elapse, or until Ctrl-C when `--seconds` is omitted.

use std::process::ExitCode;
use std::sync::atomic::{AtomicU32, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use mbrc_capture::{meta_capture_start, meta_close, meta_handshake, meta_open, Frame};
use serde::Serialize;
use tokio::fs::{File, OpenOptions};
use tokio::io::{AsyncBufReadExt, AsyncRead, AsyncWrite, AsyncWriteExt, BufReader};
use tokio::net::{TcpListener, TcpStream};
use tokio::sync::Mutex as AsyncMutex;

use crate::args::flag_value;

/// Sentinel for "no c2s seen yet on this connection".
const NO_SEQ: u64 = u64::MAX;

/// Proxy-wide context shared by every accepted connection: one capture file,
/// one global `seq`, one capture-start clock.
struct Shared {
    upstream: String,
    file: Arc<AsyncMutex<File>>,
    seq: AtomicU64,
    started: Instant,
    conn_counter: AtomicU32,
}

/// Per-connection context shared by its two direction tasks.
struct Session {
    shared: Arc<Shared>,
    conn_id: u32,
    last_c2s: AtomicU64,
    client_type: Mutex<Option<String>>,
    closed_by: Mutex<Option<&'static str>>,
}

pub fn run(args: &[String]) -> ExitCode {
    let listen = flag_value(args, "--listen").unwrap_or_else(|| "0.0.0.0:3100".to_string());
    let upstream = flag_value(args, "--upstream").unwrap_or_else(|| "127.0.0.1:3000".to_string());
    let Some(output) = flag_value(args, "--output") else {
        eprintln!(
            "usage: mbrc capture --output <file.jsonl> [--listen A] [--upstream B] [--seconds N]"
        );
        return ExitCode::from(2);
    };
    let seconds = flag_value(args, "--seconds").and_then(|s| s.parse::<u64>().ok());

    let rt = match tokio::runtime::Runtime::new() {
        Ok(rt) => rt,
        Err(e) => {
            eprintln!("runtime init failed: {e}");
            return ExitCode::FAILURE;
        }
    };
    rt.block_on(run_async(listen, upstream, output, seconds))
}

async fn run_async(
    listen: String,
    upstream: String,
    output: String,
    seconds: Option<u64>,
) -> ExitCode {
    let listener = match TcpListener::bind(&listen).await {
        Ok(l) => l,
        Err(e) => {
            eprintln!("bind {listen} failed: {e}");
            return ExitCode::FAILURE;
        }
    };

    // Truncate, not append: each capture owns a fresh `seq` starting at 0.
    let mut file = match OpenOptions::new()
        .create(true)
        .write(true)
        .truncate(true)
        .open(&output)
        .await
    {
        Ok(f) => f,
        Err(e) => {
            eprintln!("open {output} failed: {e}");
            return ExitCode::FAILURE;
        }
    };
    let header = meta_capture_start(&listen, &upstream);
    let mut line = serde_json::to_vec(&header).unwrap_or_default();
    line.push(b'\n');
    if let Err(e) = file.write_all(&line).await {
        eprintln!("write header failed: {e}");
        return ExitCode::FAILURE;
    }

    let shared = Arc::new(Shared {
        upstream,
        file: Arc::new(AsyncMutex::new(file)),
        seq: AtomicU64::new(0),
        started: Instant::now(),
        conn_counter: AtomicU32::new(0),
    });

    let accept = tokio::spawn(accept_loop(listener, Arc::clone(&shared)));
    eprintln!("capturing {listen} -> {} into {output}", shared.upstream);

    match seconds {
        Some(n) => {
            tokio::time::sleep(Duration::from_secs(n)).await;
            eprintln!("capture window ({n}s) elapsed");
        }
        None => {
            let _ = tokio::signal::ctrl_c().await;
            eprintln!("interrupted");
        }
    }
    accept.abort();
    ExitCode::SUCCESS
}

/// Serialize a record and append it as a line to the capture file.
async fn append_line<S: Serialize>(shared: &Shared, value: &S) {
    let Ok(mut line) = serde_json::to_vec(value) else {
        return;
    };
    line.push(b'\n');
    let mut f = shared.file.lock().await;
    let _ = f.write_all(&line).await;
}

async fn accept_loop(listener: TcpListener, shared: Arc<Shared>) {
    loop {
        let (client, _peer) = match listener.accept().await {
            Ok(c) => c,
            Err(e) => {
                eprintln!("accept error: {e}");
                continue;
            }
        };
        let conn_id = shared.conn_counter.fetch_add(1, Ordering::Relaxed);
        let shared = Arc::clone(&shared);
        tokio::spawn(async move {
            if let Err(e) = handle_session(client, conn_id, shared).await {
                eprintln!("conn {conn_id} ended: {e}");
            }
        });
    }
}

async fn handle_session(
    client: TcpStream,
    conn_id: u32,
    shared: Arc<Shared>,
) -> std::io::Result<()> {
    let upstream = TcpStream::connect(&shared.upstream).await?;
    client.set_nodelay(true).ok();
    upstream.set_nodelay(true).ok();

    let session = Arc::new(Session {
        shared,
        conn_id,
        last_c2s: AtomicU64::new(NO_SEQ),
        client_type: Mutex::new(None),
        closed_by: Mutex::new(None),
    });

    append_line(&session.shared, &meta_open(conn_id, "")).await;

    let (cr, cw) = client.into_split();
    let (ur, uw) = upstream.into_split();

    let s_c2s = Arc::clone(&session);
    let s_s2c = Arc::clone(&session);
    let c2s = tokio::spawn(async move { pipe_frames(BufReader::new(cr), uw, "c2s", s_c2s).await });
    let s2c = tokio::spawn(async move { pipe_frames(BufReader::new(ur), cw, "s2c", s_s2c).await });

    let result = tokio::try_join!(flatten(c2s), flatten(s2c));

    let by = session.closed_by.lock().unwrap().unwrap_or("proxy");
    let reason = match &result {
        Ok(_) => "eof".to_string(),
        Err(e) => format!("error:{e}"),
    };
    append_line(&session.shared, &meta_close(conn_id, &reason, by)).await;
    result.map(|_| ())
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
    session: Arc<Session>,
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
            {
                let mut cb = session.closed_by.lock().unwrap();
                if cb.is_none() {
                    *cb = Some(if dir == "c2s" { "client" } else { "server" });
                }
            }
            dst.shutdown().await.ok();
            return Ok(());
        }
        // Forward exact bytes first so a logging failure never stalls the wire.
        dst.write_all(&buf).await?;

        let frame = trim_line_terminator(&buf);
        if frame.is_empty() {
            continue;
        }
        record_frame(&session, dir, frame).await;
    }
}

fn trim_line_terminator(buf: &[u8]) -> &[u8] {
    let mut end = buf.len();
    while end > 0 && matches!(buf[end - 1], b'\n' | b'\r') {
        end -= 1;
    }
    &buf[..end]
}

async fn record_frame(session: &Arc<Session>, dir: &str, frame_bytes: &[u8]) {
    let seq = session.shared.seq.fetch_add(1, Ordering::Relaxed);
    let elapsed_ms = session.shared.started.elapsed().as_millis();
    let mut record = Frame::new(session.conn_id, seq, dir, elapsed_ms, frame_bytes);

    if dir == "s2c" {
        let last = session.last_c2s.load(Ordering::Relaxed);
        if last != NO_SEQ {
            record.reply_to = Some(last);
        }
    } else {
        session.last_c2s.store(seq, Ordering::Relaxed);
        maybe_handshake(session, &record).await;
    }
    append_line(&session.shared, &record).await;
}

/// Sniff `player` (client type) and `protocol` (version) c2s frames, emitting a
/// `handshake` meta record once the protocol frame arrives.
async fn maybe_handshake(session: &Arc<Session>, record: &Frame) {
    match record.context() {
        Some("player") => {
            if let Some(ct) = record
                .frame
                .as_ref()
                .and_then(|f| f.get("data"))
                .and_then(|d| d.as_str())
            {
                *session.client_type.lock().unwrap() = Some(ct.to_string());
            }
        }
        Some("protocol") => {
            let protocol_version = record
                .frame
                .as_ref()
                .and_then(|f| f.get("data"))
                .and_then(|d| d.get("protocol_version"))
                .and_then(|v| v.as_i64());
            let client_type = session.client_type.lock().unwrap().clone();
            append_line(
                &session.shared,
                &meta_handshake(session.conn_id, client_type.as_deref(), protocol_version),
            )
            .await;
        }
        _ => {}
    }
}
