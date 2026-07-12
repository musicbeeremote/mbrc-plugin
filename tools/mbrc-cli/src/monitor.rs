//! `mbrc monitor` - read-only validation client for the MBRCIP-0001 library
//! cache + now-playing paging work (see the pre-merge validation plan).
//!
//! It points at a LIVE plugin instance backed by a real library and must NEVER
//! mutate it. Every command it sends is checked against a strict read-only
//! allow-list (see `ALLOWED`); any playback / queue / rating / tag / search
//! mutation is a hard refusal, not just an omission.
//!
//! Per instance (`--concurrency N` runs N of these) it holds two connections:
//!   1. an active paging sweep that walks `browsetracks` end to end and asserts
//!      the paging invariants (stable total, no gaps/dupes, monotonic order),
//!      then cross-checks now-playing full-vs-stitched windowing;
//!   2. a mostly-idle broadcast subscriber held open for the whole run to prove
//!      the keepalive path never idle-reaps a handshaked socket.
//!
//! Both survive plugin restarts via reconnect-with-backoff, counting the drops.
//!
//! Output: one JSONL line per sweep iteration `{ts,iter,latency_ms,total,ok,
//! err,sig,...}` to `--out` (or stdout), plus a rolling summary line every
//! `--summary-secs`. A stable `sig` across a reconnect (correlated with the
//! Windows core's `rebuilt=false`) is the redb-persistence proof.
//!
//! Usage:
//!   mbrc monitor [--host H] [--port P] [--client-type Android|iOS]
//!                [--concurrency N] [--duration <dur|inf>] [--page-size N]
//!                [--summary-secs N] [--req-timeout-ms N] [--out FILE]

use std::collections::HashSet;
use std::io::{self, Write};
use std::process::ExitCode;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use mbrc_wire::{frame_line, parse_context, ClientHandshake, FrameAccumulator};
use serde_json::{json, Value};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::tcp::{OwnedReadHalf, OwnedWriteHalf};
use tokio::net::TcpStream;

use crate::args::flag_value;

/// Contexts the monitor is permitted to send. STRICTLY read-only: pointing at a
/// real library, it must never mutate. Anything not here (play/pause/stop/next/
/// prev, volume/position/output, queue, np play/remove/move/SEARCH, set rating/
/// lfm/tag, play-all, playlist play) is refused before the frame leaves.
const ALLOWED: &[&str] = &[
    "playerstatus",
    "nowplayingtrack",
    "nowplayingdetails",
    "nowplayingcover",
    "nowplayinglyrics",
    "browsegenres",
    "browseartists",
    "browsealbums",
    "browsetracks",
    "librarygenreartists",
    "libraryartistalbums",
    "libraryalbumtracks",
    "libraryalbumcover",
    "nowplayinglist",
    "playlistlist",
    "pluginversion",
];

fn is_read_only(context: &str) -> bool {
    ALLOWED.contains(&context)
}

struct Cfg {
    host: String,
    port: u16,
    client_type: String,
    concurrency: usize,
    duration: Option<Duration>, // None = infinite
    page_size: i32,
    summary: Duration,
    req_timeout: Duration,
}

/// Shared counters for the rolling summary line.
#[derive(Default)]
struct Stats {
    sweeps: AtomicU64,
    invariant_fail: AtomicU64,
    req_ok: AtomicU64,
    req_err: AtomicU64,
    reconnects: AtomicU64,
    idle_events: AtomicU64,
    sig_drift: AtomicU64,
    // First library signature any worker observed; later mismatches count as
    // drift (real change or, if it flips back after a reconnect, a rebuild).
    first_sig: AtomicU64,
}

impl Stats {
    /// Record the first-seen signature or flag drift from it. Returns true when
    /// the value differs from the established baseline.
    fn note_sig(&self, sig: u64) -> bool {
        // 0 is used as the "unset" sentinel; a real 0 signature (empty library)
        // just never establishes a baseline, which is fine for this check.
        match self
            .first_sig
            .compare_exchange(0, sig, Ordering::SeqCst, Ordering::SeqCst)
        {
            Ok(_) => false,           // we set the baseline
            Err(prev) => prev != sig, // baseline already set; is this a drift?
        }
    }
}

/// Append-or-stdout JSONL sink shared across workers (locked per line).
struct Sink {
    inner: Mutex<Box<dyn Write + Send>>,
}

impl Sink {
    fn line(&self, v: &Value) {
        if let Ok(mut w) = self.inner.lock() {
            let _ = writeln!(w, "{v}");
            let _ = w.flush();
        }
    }
}

fn now_millis() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

/// FNV-1a over a path list - the same hash family the core uses for its library
/// fingerprint. Order-sensitive, which is what we want for a browse-order sig.
fn fnv1a_paths(paths: &[String]) -> u64 {
    let mut h: u64 = 0xcbf2_9ce4_8422_2325;
    for p in paths {
        for b in p.as_bytes() {
            h ^= u64::from(*b);
            h = h.wrapping_mul(0x0000_0100_0000_01b3);
        }
        h ^= u64::from(b'\n');
        h = h.wrapping_mul(0x0000_0100_0000_01b3);
    }
    h
}

/// Library signature = total folded together with the first-page and last-page
/// path hashes. Cheap, stable if the library is unchanged, and sensitive to any
/// reorder at either end. Used for the persistence-across-restart check.
fn library_signature(total: i32, first_page: &[String], last_page: &[String]) -> u64 {
    let mut h = fnv1a_paths(first_page);
    h ^= fnv1a_paths(last_page).rotate_left(32);
    h ^= (total as u64).wrapping_mul(0x0000_0100_0000_01b3);
    h
}

// ── Connection ──

struct Conn {
    rd: OwnedReadHalf,
    wr: OwnedWriteHalf,
    acc: FrameAccumulator,
    hs: ClientHandshake,
}

impl Conn {
    /// Connect and drive the client handshake to completion (`player` echo ->
    /// `protocol`). `no_broadcast=true` for the paging sweep (quiet channel),
    /// false for the idle subscriber (wants the broadcast stream).
    async fn connect(cfg: &Cfg, no_broadcast: bool) -> io::Result<Conn> {
        let stream = TcpStream::connect((cfg.host.as_str(), cfg.port)).await?;
        stream.set_nodelay(true).ok();
        let (rd, wr) = stream.into_split();
        let mut c = Conn {
            rd,
            wr,
            acc: FrameAccumulator::default(),
            hs: ClientHandshake::new(cfg.client_type.clone(), 4, no_broadcast),
        };
        let initial = c.hs.initial();
        c.send_raw(&initial).await?;

        let deadline = Instant::now() + Duration::from_secs(10);
        loop {
            match c.recv_frame(deadline).await? {
                None => return Err(io::Error::new(io::ErrorKind::TimedOut, "handshake timeout")),
                Some(line) => {
                    let ctx = parse_context(&line).unwrap_or_default();
                    let was_player = ctx == mbrc_wire::CTX_PLAYER;
                    if let Some(reply) = c.hs.on_incoming(&ctx) {
                        c.send_raw(&reply).await?;
                        // Answering the server's `player` echo with `protocol`
                        // is the last handshake step; commands are accepted now.
                        if was_player {
                            return Ok(c);
                        }
                    }
                }
            }
        }
    }

    async fn send_raw(&mut self, line: &str) -> io::Result<()> {
        self.wr.write_all(frame_line(line).as_bytes()).await?;
        self.wr.flush().await
    }

    /// Pop the next complete frame, reading from the socket as needed until
    /// `deadline`. `Ok(None)` on timeout; `Err` on a closed/broken connection.
    async fn recv_frame(&mut self, deadline: Instant) -> io::Result<Option<String>> {
        loop {
            if let Some(line) = self.acc.next_frame() {
                return Ok(Some(line));
            }
            let remaining = deadline.saturating_duration_since(Instant::now());
            if remaining.is_zero() {
                return Ok(None);
            }
            let mut buf = [0u8; 8192];
            match tokio::time::timeout(remaining, self.rd.read(&mut buf)).await {
                Err(_) => return Ok(None),
                Ok(Ok(0)) => {
                    return Err(io::Error::new(io::ErrorKind::UnexpectedEof, "peer closed"))
                }
                Ok(Ok(n)) => self.acc.push_bytes(&buf[..n]),
                Ok(Err(e)) => return Err(e),
            }
        }
    }

    /// Handle one raw frame: auto-answer handshake/ping (returns `None`), or hand
    /// back a real reply's `(context, data)` for the caller.
    async fn handle_frame(&mut self, line: &str) -> io::Result<Option<(String, Value)>> {
        if line.trim().is_empty() {
            return Ok(None);
        }
        let ctx = parse_context(line).unwrap_or_default();
        if let Some(reply) = self.hs.on_incoming(&ctx) {
            self.send_raw(&reply).await?;
            return Ok(None);
        }
        let data = serde_json::from_str::<Value>(line)
            .ok()
            .and_then(|v| v.get("data").cloned())
            .unwrap_or(Value::Null);
        Ok(Some((ctx, data)))
    }

    /// Send a read-only command and return the `data` of the reply that comes
    /// back on the same context. Pings/handshake and any interleaved broadcast
    /// frames are handled/skipped meanwhile. Refuses non-allow-listed contexts.
    async fn request(
        &mut self,
        context: &str,
        data: Value,
        timeout: Duration,
    ) -> io::Result<Value> {
        if !is_read_only(context) {
            return Err(io::Error::new(
                io::ErrorKind::PermissionDenied,
                format!("refusing non-read-only command: {context}"),
            ));
        }
        let cmd = json!({ "context": context, "data": data }).to_string();
        self.send_raw(&cmd).await?;
        let deadline = Instant::now() + timeout;
        loop {
            match self.recv_frame(deadline).await? {
                None => {
                    return Err(io::Error::new(
                        io::ErrorKind::TimedOut,
                        format!("no reply for {context}"),
                    ))
                }
                Some(line) => {
                    if let Some((ctx, payload)) = self.handle_frame(&line).await? {
                        if ctx == context {
                            return Ok(payload);
                        }
                        // A broadcast slipped in while we waited; ignore it.
                    }
                }
            }
        }
    }
}

// ── Invariant checks ──

struct SweepResult {
    total: i32,
    pages: i32,
    signature: u64,
    errors: Vec<String>,
}

/// Walk `browsetracks` from 0 to `total` in `page_size` windows and assert the
/// paging invariants: total stable across pages, echoed offset matches, no
/// gaps/dupes in `src`, and the union size equals `total`.
async fn sweep_browsetracks(
    conn: &mut Conn,
    page_size: i32,
    timeout: Duration,
) -> io::Result<SweepResult> {
    let mut offset = 0;
    let mut total: Option<i32> = None;
    let mut seen: HashSet<String> = HashSet::new();
    let mut errors = Vec::new();
    let mut first_page: Vec<String> = Vec::new();
    let mut last_page: Vec<String> = Vec::new();
    let mut pages = 0;

    loop {
        let data = conn
            .request(
                "browsetracks",
                json!({ "offset": offset, "limit": page_size }),
                timeout,
            )
            .await?;
        pages += 1;

        let t = data.get("total").and_then(Value::as_i64).unwrap_or(-1) as i32;
        match total {
            None => total = Some(t),
            Some(prev) if prev != t => {
                errors.push(format!("total changed {prev}->{t} at offset {offset}"))
            }
            _ => {}
        }

        let rep_off = data.get("offset").and_then(Value::as_i64).unwrap_or(-1) as i32;
        if rep_off != offset {
            errors.push(format!("offset echo {rep_off} != requested {offset}"));
        }

        let items = data
            .get("data")
            .and_then(Value::as_array)
            .cloned()
            .unwrap_or_default();
        if items.is_empty() {
            break;
        }

        let srcs: Vec<String> = items
            .iter()
            .filter_map(|it| it.get("src").and_then(Value::as_str).map(str::to_string))
            .collect();
        if srcs.len() != items.len() {
            errors.push(format!(
                "page at {offset}: {} items missing src",
                items.len() - srcs.len()
            ));
        }
        if offset == 0 {
            first_page = srcs.clone();
        }
        last_page = srcs.clone();

        for s in srcs {
            if !seen.insert(s.clone()) {
                errors.push(format!("duplicate src at offset {offset}: {s}"));
            }
        }

        offset += items.len() as i32;
        if let Some(t) = total {
            if t >= 0 && offset >= t {
                break;
            }
        }
    }

    let total = total.unwrap_or(0).max(0);
    if seen.len() as i32 != total {
        errors.push(format!(
            "union {} != total {} (gap or dupe)",
            seen.len(),
            total
        ));
    }
    let signature = library_signature(total, &first_page, &last_page);
    Ok(SweepResult {
        total,
        pages,
        signature,
        errors,
    })
}

/// Extract the ordered `(position, path)` tuples from a now-playing list reply.
fn nplist_items(data: &Value) -> Vec<(i64, String)> {
    data.get("data")
        .and_then(Value::as_array)
        .map(|arr| {
            arr.iter()
                .map(|it| {
                    let pos = it.get("position").and_then(Value::as_i64).unwrap_or(-1);
                    let path = it
                        .get("path")
                        .and_then(Value::as_str)
                        .unwrap_or_default()
                        .to_string();
                    (pos, path)
                })
                .collect()
        })
        .unwrap_or_default()
}

/// Cross-check now-playing windowing: a one-shot full read must equal the
/// page-stitched read (same ordered items). This is the check that catches an
/// offset/window bug in the source-side paging. Returns any mismatches; an empty
/// now-playing list is skipped (nothing to page).
async fn check_nowplaying_windowing(
    conn: &mut Conn,
    page_size: i32,
    timeout: Duration,
) -> io::Result<Vec<String>> {
    let head = conn
        .request(
            "nowplayinglist",
            json!({ "offset": 0, "limit": page_size }),
            timeout,
        )
        .await?;
    let total = head.get("total").and_then(Value::as_i64).unwrap_or(0) as i32;
    if total <= 0 {
        return Ok(Vec::new());
    }

    let full = conn
        .request(
            "nowplayinglist",
            json!({ "offset": 0, "limit": total }),
            timeout,
        )
        .await?;
    let full_items = nplist_items(&full);

    let mut stitched: Vec<(i64, String)> = Vec::new();
    let mut offset = 0;
    while offset < total {
        let pg = conn
            .request(
                "nowplayinglist",
                json!({ "offset": offset, "limit": page_size }),
                timeout,
            )
            .await?;
        let items = nplist_items(&pg);
        if items.is_empty() {
            break;
        }
        offset += items.len() as i32;
        stitched.extend(items);
    }

    let mut errors = Vec::new();
    if full_items != stitched {
        errors.push(format!(
            "nowplaying full({}) != stitched({}) items",
            full_items.len(),
            stitched.len()
        ));
    }
    Ok(errors)
}

// ── Workers ──

/// One instance's paging loop: (re)connect, sweep + windowing check, emit a
/// JSONL line, repeat until the deadline. Reconnects with capped backoff so it
/// survives plugin restarts, counting each drop.
async fn pager_loop(
    id: usize,
    cfg: Arc<Cfg>,
    stats: Arc<Stats>,
    sink: Arc<Sink>,
    until: Option<Instant>,
) {
    let mut iter: u64 = 0;
    let mut backoff = Duration::from_millis(500);
    let mut conn: Option<Conn> = None;

    loop {
        if let Some(t) = until {
            if Instant::now() >= t {
                return;
            }
        }

        // Ensure a live connection.
        if conn.is_none() {
            match Conn::connect(&cfg, true).await {
                Ok(c) => {
                    conn = Some(c);
                    backoff = Duration::from_millis(500);
                }
                Err(e) => {
                    sink.line(&json!({
                        "ts": now_millis(), "worker": id, "event": "connect_fail",
                        "err": e.to_string(),
                    }));
                    tokio::time::sleep(backoff).await;
                    backoff = (backoff * 2).min(Duration::from_secs(8));
                    continue;
                }
            }
        }

        let c = conn.as_mut().unwrap();
        iter += 1;
        let start = Instant::now();
        let sweep = sweep_browsetracks(c, cfg.page_size, cfg.req_timeout).await;

        match sweep {
            Ok(mut res) => {
                let np = check_nowplaying_windowing(c, cfg.page_size, cfg.req_timeout)
                    .await
                    .unwrap_or_else(|e| vec![format!("nowplaying check io: {e}")]);
                res.errors.extend(np);

                let latency = start.elapsed().as_millis() as u64;
                let err_count = res.errors.len() as u64;
                stats.sweeps.fetch_add(1, Ordering::Relaxed);
                if err_count > 0 {
                    stats.invariant_fail.fetch_add(1, Ordering::Relaxed);
                    stats.req_err.fetch_add(err_count, Ordering::Relaxed);
                } else {
                    stats.req_ok.fetch_add(1, Ordering::Relaxed);
                }
                let drift = stats.note_sig(res.signature);
                if drift {
                    stats.sig_drift.fetch_add(1, Ordering::Relaxed);
                }

                sink.line(&json!({
                    "ts": now_millis(),
                    "worker": id,
                    "iter": iter,
                    "latency_ms": latency,
                    "total": res.total,
                    "pages": res.pages,
                    "ok": err_count == 0,
                    "err": res.errors,
                    "sig": format!("{:016x}", res.signature),
                    "sig_drift": drift,
                }));
            }
            Err(e) => {
                // Connection-level failure: drop it, count a reconnect, back off.
                stats.reconnects.fetch_add(1, Ordering::Relaxed);
                sink.line(&json!({
                    "ts": now_millis(), "worker": id, "iter": iter,
                    "event": "sweep_io", "err": e.to_string(),
                }));
                conn = None;
                tokio::time::sleep(backoff).await;
                backoff = (backoff * 2).min(Duration::from_secs(8));
            }
        }
    }
}

/// One instance's idle broadcast subscriber: hold the socket open, answer pings,
/// count broadcast frames, and never send a command. Proves a handshaked
/// subscriber is not idle-reaped. Reconnects on drop like the pager.
async fn subscriber_loop(
    id: usize,
    cfg: Arc<Cfg>,
    stats: Arc<Stats>,
    sink: Arc<Sink>,
    until: Option<Instant>,
) {
    let mut backoff = Duration::from_millis(500);
    loop {
        if let Some(t) = until {
            if Instant::now() >= t {
                return;
            }
        }

        let mut conn = match Conn::connect(&cfg, false).await {
            Ok(c) => {
                backoff = Duration::from_millis(500);
                c
            }
            Err(_) => {
                tokio::time::sleep(backoff).await;
                backoff = (backoff * 2).min(Duration::from_secs(8));
                continue;
            }
        };

        loop {
            let now = Instant::now();
            if until.map(|t| now >= t).unwrap_or(false) {
                return;
            }
            // Wake at least every 30s to re-check the deadline; pings are answered
            // inside handle_frame as they arrive.
            let deadline = until
                .map(|t| (now + Duration::from_secs(30)).min(t))
                .unwrap_or(now + Duration::from_secs(30));
            match conn.recv_frame(deadline).await {
                Ok(None) => {} // idle tick, still connected
                Ok(Some(line)) => {
                    if let Ok(Some(_)) = conn.handle_frame(&line).await {
                        stats.idle_events.fetch_add(1, Ordering::Relaxed);
                    }
                }
                Err(_) => {
                    // Reaped or restarted: this is what we are watching for.
                    stats.reconnects.fetch_add(1, Ordering::Relaxed);
                    sink.line(&json!({
                        "ts": now_millis(), "worker": id, "event": "subscriber_drop",
                    }));
                    break;
                }
            }
        }
    }
}

/// Periodic rolling summary line until the deadline.
async fn summary_loop(cfg: Arc<Cfg>, stats: Arc<Stats>, sink: Arc<Sink>, until: Option<Instant>) {
    loop {
        tokio::time::sleep(cfg.summary).await;
        if until.map(|t| Instant::now() >= t).unwrap_or(false) {
            break;
        }
        sink.line(&json!({
            "ts": now_millis(),
            "event": "summary",
            "sweeps": stats.sweeps.load(Ordering::Relaxed),
            "invariant_fail": stats.invariant_fail.load(Ordering::Relaxed),
            "req_ok": stats.req_ok.load(Ordering::Relaxed),
            "req_err": stats.req_err.load(Ordering::Relaxed),
            "reconnects": stats.reconnects.load(Ordering::Relaxed),
            "idle_events": stats.idle_events.load(Ordering::Relaxed),
            "sig_drift": stats.sig_drift.load(Ordering::Relaxed),
            "sig": format!("{:016x}", stats.first_sig.load(Ordering::Relaxed)),
        }));
    }
}

// ── Entry ──

fn parse_duration(s: &str) -> Option<Option<Duration>> {
    let s = s.trim();
    if s.eq_ignore_ascii_case("inf") || s.eq_ignore_ascii_case("infinite") {
        return Some(None);
    }
    let (num, mult) = match s.chars().last() {
        Some('s') => (&s[..s.len() - 1], 1),
        Some('m') => (&s[..s.len() - 1], 60),
        Some('h') => (&s[..s.len() - 1], 3600),
        Some(c) if c.is_ascii_digit() => (s, 1),
        _ => return None,
    };
    num.parse::<u64>()
        .ok()
        .map(|n| Some(Duration::from_secs(n * mult)))
}

fn parse_u64(args: &[String], flag: &str, default: u64) -> Result<u64, ExitCode> {
    match flag_value(args, flag) {
        None => Ok(default),
        Some(v) => v.parse::<u64>().map_err(|_| {
            eprintln!("{flag} must be a number");
            ExitCode::from(2)
        }),
    }
}

pub fn run(args: &[String]) -> ExitCode {
    let host = flag_value(args, "--host").unwrap_or_else(|| "127.0.0.1".to_string());
    let port = match parse_u64(args, "--port", 3000) {
        Ok(p) => p as u16,
        Err(c) => return c,
    };
    let client_type = flag_value(args, "--client-type").unwrap_or_else(|| "Android".to_string());
    if !matches!(client_type.as_str(), "Android" | "iOS") {
        eprintln!("--client-type must be Android or iOS");
        return ExitCode::from(2);
    }
    let concurrency = match parse_u64(args, "--concurrency", 1) {
        Ok(n) => n.max(1) as usize,
        Err(c) => return c,
    };
    let page_size = match parse_u64(args, "--page-size", 100) {
        Ok(n) => n.clamp(1, 10_000) as i32,
        Err(c) => return c,
    };
    let summary = match parse_u64(args, "--summary-secs", 60) {
        Ok(n) => Duration::from_secs(n.max(1)),
        Err(c) => return c,
    };
    let req_timeout = match parse_u64(args, "--req-timeout-ms", 15_000) {
        Ok(n) => Duration::from_millis(n.max(100)),
        Err(c) => return c,
    };
    let duration = match flag_value(args, "--duration") {
        None => Some(Duration::from_secs(60)),
        Some(v) => match parse_duration(&v) {
            Some(d) => d,
            None => {
                eprintln!("--duration must be inf or a number with optional s/m/h suffix");
                return ExitCode::from(2);
            }
        },
    };

    let sink: Box<dyn Write + Send> = match flag_value(args, "--out") {
        Some(path) => match std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(&path)
        {
            Ok(f) => Box::new(f),
            Err(e) => {
                eprintln!("cannot open --out {path}: {e}");
                return ExitCode::FAILURE;
            }
        },
        None => Box::new(io::stdout()),
    };

    let cfg = Arc::new(Cfg {
        host,
        port,
        client_type,
        concurrency,
        duration,
        page_size,
        summary,
        req_timeout,
    });
    let stats = Arc::new(Stats::default());
    let sink = Arc::new(Sink {
        inner: Mutex::new(sink),
    });

    let rt = match tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()
    {
        Ok(rt) => rt,
        Err(e) => {
            eprintln!("runtime init failed: {e}");
            return ExitCode::FAILURE;
        }
    };

    rt.block_on(run_async(cfg, stats, sink))
}

async fn run_async(cfg: Arc<Cfg>, stats: Arc<Stats>, sink: Arc<Sink>) -> ExitCode {
    let until = cfg.duration.map(|d| Instant::now() + d);

    sink.line(&json!({
        "ts": now_millis(),
        "event": "start",
        "host": cfg.host,
        "port": cfg.port,
        "client_type": cfg.client_type,
        "concurrency": cfg.concurrency,
        "page_size": cfg.page_size,
        "duration": cfg.duration.map(|d| d.as_secs()),
    }));

    let mut handles = Vec::new();
    for id in 0..cfg.concurrency {
        handles.push(tokio::spawn(pager_loop(
            id,
            cfg.clone(),
            stats.clone(),
            sink.clone(),
            until,
        )));
        handles.push(tokio::spawn(subscriber_loop(
            id,
            cfg.clone(),
            stats.clone(),
            sink.clone(),
            until,
        )));
    }
    let summary = tokio::spawn(summary_loop(
        cfg.clone(),
        stats.clone(),
        sink.clone(),
        until,
    ));

    if until.is_some() {
        for h in handles {
            let _ = h.await;
        }
        summary.abort();
    } else {
        // Infinite run: wait until interrupted.
        let _ = tokio::signal::ctrl_c().await;
        for h in handles {
            h.abort();
        }
        summary.abort();
    }

    // Final summary.
    let fails = stats.invariant_fail.load(Ordering::Relaxed);
    sink.line(&json!({
        "ts": now_millis(),
        "event": "end",
        "sweeps": stats.sweeps.load(Ordering::Relaxed),
        "invariant_fail": fails,
        "req_ok": stats.req_ok.load(Ordering::Relaxed),
        "req_err": stats.req_err.load(Ordering::Relaxed),
        "reconnects": stats.reconnects.load(Ordering::Relaxed),
        "idle_events": stats.idle_events.load(Ordering::Relaxed),
        "sig_drift": stats.sig_drift.load(Ordering::Relaxed),
        "sig": format!("{:016x}", stats.first_sig.load(Ordering::Relaxed)),
    }));

    if fails > 0 {
        ExitCode::FAILURE
    } else {
        ExitCode::SUCCESS
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn allow_list_excludes_mutations() {
        for c in [
            "playerplay",
            "playerpause",
            "playerstop",
            "playernext",
            "playervolume",
            "playeroutputswitch",
            "nowplayinglistplay",
            "nowplayinglistremove",
            "nowplayinglistmove",
            "nowplayinglistsearch",
            "nowplayingrating",
            "nowplayingtagchange",
            "libraryplayall",
            "playlistplay",
        ] {
            assert!(!is_read_only(c), "{c} must not be read-only");
        }
        for c in [
            "browsetracks",
            "nowplayinglist",
            "pluginversion",
            "playerstatus",
        ] {
            assert!(is_read_only(c), "{c} should be read-only");
        }
    }

    #[test]
    fn signature_is_order_sensitive() {
        let a = vec!["a".to_string(), "b".to_string()];
        let b = vec!["b".to_string(), "a".to_string()];
        assert_ne!(library_signature(2, &a, &a), library_signature(2, &b, &b));
        assert_eq!(library_signature(2, &a, &a), library_signature(2, &a, &a));
    }

    #[test]
    fn parses_durations() {
        assert_eq!(parse_duration("inf"), Some(None));
        assert_eq!(parse_duration("30"), Some(Some(Duration::from_secs(30))));
        assert_eq!(parse_duration("5m"), Some(Some(Duration::from_secs(300))));
        assert_eq!(parse_duration("2h"), Some(Some(Duration::from_secs(7200))));
        assert_eq!(parse_duration("bogus"), None);
    }

    #[test]
    fn nplist_items_reads_position_and_path() {
        let v = json!({ "data": [
            { "position": 0, "path": "x" },
            { "position": 1, "path": "y" },
        ]});
        assert_eq!(
            nplist_items(&v),
            vec![(0, "x".to_string()), (1, "y".to_string())]
        );
    }

    #[test]
    fn note_sig_flags_drift_not_baseline() {
        let s = Stats::default();
        assert!(!s.note_sig(42)); // baseline
        assert!(!s.note_sig(42)); // same
        assert!(s.note_sig(99)); // drift
    }
}
