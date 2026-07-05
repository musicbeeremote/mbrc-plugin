//! `mbrc fuzz` - a seeded protocol fuzzer for a V4 server.
//!
//! Generates a deterministic sequence of inputs from `--seed` and fires them at
//! a target, recording every input/response to an `mbrc-capture/2` trace and
//! flagging anomalies (connection drop, non-JSON reply, liveness-probe failure).
//! Three generators are mixed:
//!   * **structural** - a known context with edge-case / wrong-type `data`;
//!   * **malformed**  - invalid JSON, bad framing, oversized, iOS quirks;
//!   * **mutation**   - `--corpus <golden>` frames with their `data` mutated.
//!
//! **Read-only by default**: structural/mutation only target query-shaped
//! contexts, so it's safe against a real library. `--destructive` adds the
//! reversible player state (volume/mute/scrobbler), snapshotting it via
//! `playerstatus` before the run and restoring it after; other state-changing
//! commands stay excluded until a scratch library + fingerprint (Phase 2b).
//!
//! `--diff-host H2` runs the same inputs against a second target and diffs the
//! responses (the parity oracle; against `mbrc-core` in Milestone B).
//! `--save-script F` freezes the generated inputs as a replayable c2s script.
//!
//! Usage:
//!   mbrc fuzz [--host H] [--port P] [--seed N] [--iterations K] [--wait-ms N]
//!             [--corpus <file|dir>] [--out F] [--diff-host H2] [--diff-port P2]
//!             [--destructive] [--save-script F]

use std::process::ExitCode;
use std::time::Duration;

use mbrc_capture::{parse_line, Frame, Record};
use mbrc_wire::{frame_line, parse_context, ClientHandshake, FrameAccumulator, CTX_PLAYER};
use serde_json::{Map, Value};
use tokio::io::{AsyncReadExt, AsyncWrite, AsyncWriteExt};
use tokio::net::TcpStream;

use crate::args::{flag_value, has_flag};
use crate::compare::diff_report;
use crate::rng::Rng;
use crate::trim::read_all;

/// Query-shaped contexts (getters / searches / browses), safe against a real
/// library. Wire names are the authoritative V4 `ProtocolConstants`.
const READONLY_CONTEXTS: &[&str] = &[
    "playerstatus",
    "playeroutput",
    "pluginversion",
    "nowplayingtrack",
    "nowplayingdetails",
    "nowplayingcover",
    "nowplayinglyrics",
    "nowplayingposition",
    "nowplayingrating",
    "nowplayinglist",
    "playlistlist",
    "librarysearchartist",
    "librarysearchalbum",
    "librarysearchgenre",
    "librarysearchtitle",
    "libraryartistalbums",
    "librarygenreartists",
    "libraryalbumtracks",
    "browsegenres",
    "browseartists",
    "browsealbums",
    "browsetracks",
    "radiostations",
    "libraryalbumcover",
    "librarycovercachebuildstatus",
];

/// State-changing contexts that snapshot/restore can cleanly reverse: their
/// values read back from `playerstatus` and set with the same type. Only fuzzed
/// under `--destructive`, wrapped in a snapshot/restore. Other commands
/// (playback nav, queue, tag writes, playlists) are excluded until a scratch
/// library + fingerprint land (Phase 2b).
const REVERSIBLE_CONTEXTS: &[&str] = &["playervolume", "playermute", "scrobbler"];

/// Cap oversized payloads so we stress buffers without risking an OOM on the
/// host.
const MAX_BLOB: usize = 50_000;

/// One generated input: the exact bytes to send plus a human note for reports.
struct Input {
    bytes: Vec<u8>,
    note: String,
}

pub fn run(args: &[String]) -> ExitCode {
    let host = flag_value(args, "--host").unwrap_or_else(|| "127.0.0.1".to_string());
    let port = parse_port(flag_value(args, "--port"), 3000);
    let seed: u64 = flag_value(args, "--seed")
        .and_then(|s| s.parse().ok())
        .unwrap_or(1);
    let iterations: usize = flag_value(args, "--iterations")
        .and_then(|s| s.parse().ok())
        .unwrap_or(200);
    let wait = Duration::from_millis(
        flag_value(args, "--wait-ms")
            .and_then(|s| s.parse().ok())
            .unwrap_or(300),
    );
    let destructive = has_flag(args, "--destructive");
    let out_path = flag_value(args, "--out");
    let diff_host = flag_value(args, "--diff-host");
    let diff_port = parse_port(flag_value(args, "--diff-port"), port);

    if destructive {
        eprintln!(
            "--destructive: fuzzing reversible player state ({}); snapshotting via playerstatus \
             and restoring afterward.",
            REVERSIBLE_CONTEXTS.join(", ")
        );
    }

    let corpus = flag_value(args, "--corpus")
        .and_then(|p| read_all(&p).ok())
        .map(|c| load_corpus(&c))
        .unwrap_or_default();

    let mut rng = Rng::new(seed);
    let inputs = generate(&mut rng, iterations, &corpus, destructive);

    // Freeze the generated inputs as a replayable c2s script.
    if let Some(path) = flag_value(args, "--save-script") {
        let script: Vec<String> = inputs
            .iter()
            .enumerate()
            .map(|(i, inp)| {
                let raw = String::from_utf8_lossy(&inp.bytes);
                let mut f = Frame::new(
                    0,
                    i as u64,
                    "c2s",
                    0,
                    raw.trim_end_matches(['\r', '\n']).as_bytes(),
                );
                // A frozen script must be byte-stable for a given seed, so pin
                // the otherwise-wall-clock timestamp.
                f.ts = "1970-01-01T00:00:00Z".to_string();
                serde_json::to_string(&f).unwrap_or_default()
            })
            .collect();
        let _ = std::fs::write(&path, script.join("\n"));
        println!("wrote {} input frames to {path}", inputs.len());
    }

    let rt = match tokio::runtime::Runtime::new() {
        Ok(rt) => rt,
        Err(e) => {
            eprintln!("runtime init failed: {e}");
            return ExitCode::FAILURE;
        }
    };

    let result = rt.block_on(async {
        // Snapshot reversible player state before a destructive run, restore after.
        let snapshot = if destructive {
            snapshot_player(&host, port, wait).await?
        } else {
            None
        };
        let a = drive(&host, port, &inputs, wait).await?;
        let b = match &diff_host {
            Some(h) => Some(drive(h, diff_port, &inputs, wait).await?),
            None => None,
        };
        if let Some(snap) = &snapshot {
            restore_player(&host, port, wait, snap).await?;
        }
        Ok::<_, std::io::Error>((a, b, snapshot))
    });

    let ((lines_a, anomalies_a), b, snapshot) = match result {
        Ok(v) => v,
        Err(e) => {
            eprintln!("fuzz run failed: {e}");
            return ExitCode::FAILURE;
        }
    };

    if let Some(snap) = &snapshot {
        println!("restored player state: {snap:?}");
    }

    if let Some(path) = &out_path {
        let _ = std::fs::write(path, lines_a.join("\n"));
    }

    println!(
        "fuzzed {} input(s) against {host}:{port} (seed {seed})",
        inputs.len()
    );
    report_anomalies("target A", &anomalies_a);

    let mut failed = !anomalies_a.is_empty();

    if let Some((lines_b, anomalies_b)) = b {
        report_anomalies("target B", &anomalies_b);
        println!("\n-- differential diff (A vs B) --");
        // Drop ping/pong keepalives - their presence is timing-dependent, not a
        // real behavioral divergence between the two targets.
        let differing = diff_report(
            &strip_keepalive(&lines_a),
            &strip_keepalive(&lines_b),
            false,
            &[],
        );
        failed = failed || !anomalies_b.is_empty() || differing > 0;
    }

    println!("\nseed {seed} reproduces this run.");
    if failed {
        ExitCode::FAILURE
    } else {
        ExitCode::SUCCESS
    }
}

fn parse_port(v: Option<String>, default: u16) -> u16 {
    v.and_then(|s| s.parse().ok()).unwrap_or(default)
}

/// Drop ping/pong keepalive frames (timing noise) before a differential diff.
fn strip_keepalive(lines: &[String]) -> String {
    lines
        .iter()
        .filter(|l| {
            let ctx = match parse_line(l) {
                Some(Record::Frame(f)) => f.context().map(str::to_owned),
                _ => None,
            };
            !matches!(ctx.as_deref(), Some("ping") | Some("pong"))
        })
        .cloned()
        .collect::<Vec<_>>()
        .join("\n")
}

fn report_anomalies(label: &str, anomalies: &[String]) {
    if anomalies.is_empty() {
        println!("{label}: no anomalies");
    } else {
        println!("{label}: {} anomaly(ies)", anomalies.len());
        for a in anomalies {
            println!("  ! {a}");
        }
    }
}

/// Extract `(context, data)` of read-only `c2s` frames from a golden corpus.
fn load_corpus(contents: &str) -> Vec<(String, Value)> {
    contents
        .lines()
        .filter_map(|l| match parse_line(l) {
            Some(Record::Frame(f)) if f.dir == "c2s" => {
                let ctx = f.context()?.to_string();
                if !READONLY_CONTEXTS.contains(&ctx.as_str()) {
                    return None;
                }
                let data = f.frame.as_ref().and_then(|fr| fr.get("data")).cloned();
                Some((ctx, data.unwrap_or(Value::Null)))
            }
            _ => None,
        })
        .collect()
}

// -- Generation ------------------------------------------------------------

fn generate(
    rng: &mut Rng,
    iterations: usize,
    corpus: &[(String, Value)],
    destructive: bool,
) -> Vec<Input> {
    (0..iterations)
        .map(|_| {
            // Weight: ~40% structural, ~40% malformed, ~20% mutation (if corpus).
            match rng.below(5) {
                0 | 1 => gen_structural(rng, destructive),
                2 | 3 => gen_malformed(rng),
                _ if !corpus.is_empty() => gen_mutation(rng, corpus),
                _ => gen_structural(rng, destructive),
            }
        })
        .collect()
}

fn context_pool(destructive: bool) -> Vec<&'static str> {
    let mut v = READONLY_CONTEXTS.to_vec();
    if destructive {
        v.extend_from_slice(REVERSIBLE_CONTEXTS);
    }
    v
}

fn gen_structural(rng: &mut Rng, destructive: bool) -> Input {
    let pool = context_pool(destructive);
    let ctx = *rng.choice(&pool);
    let data = random_value(rng, 3);
    let frame = serde_json::json!({ "context": ctx, "data": data });
    let line = serde_json::to_string(&frame).unwrap_or_default();
    Input {
        bytes: frame_line(&line).into_bytes(),
        note: format!("structural {ctx}"),
    }
}

fn gen_mutation(rng: &mut Rng, corpus: &[(String, Value)]) -> Input {
    let (ctx, data) = rng.choice(corpus);
    let mut data = data.clone();
    mutate_value(rng, &mut data);
    let frame = serde_json::json!({ "context": ctx, "data": data });
    let line = serde_json::to_string(&frame).unwrap_or_default();
    Input {
        bytes: frame_line(&line).into_bytes(),
        note: format!("mutation {ctx}"),
    }
}

fn gen_malformed(rng: &mut Rng) -> Input {
    let (bytes, note): (Vec<u8>, &str) = match rng.below(11) {
        0 => (b"not json at all\r\n".to_vec(), "non-json"),
        1 => (
            b"{\"context\":\"playerstatus\"\r\n".to_vec(),
            "unterminated-object",
        ),
        2 => (b"{context:playerstatus}\r\n".to_vec(), "unquoted-keys"),
        3 => (b"{\"context\":123}\r\n".to_vec(), "non-string-context"),
        4 => (b"{}\r\n".to_vec(), "empty-object"),
        5 => (b"[]\r\n".to_vec(), "array-not-object"),
        6 => (
            b"{\"context\":\"nowplayinglyrics\",\"data\":\"it\\'s\"}\r\n".to_vec(),
            "ios-quote-quirk",
        ),
        7 => (b"{\"data\":status}\r\n".to_vec(), "ios-bare-identifier"),
        8 => {
            // Oversized string payload.
            let blob = "A".repeat(1 + rng.below(MAX_BLOB));
            (
                format!("{{\"context\":\"playerstatus\",\"data\":\"{blob}\"}}\r\n").into_bytes(),
                "oversized",
            )
        }
        9 => {
            // Valid frame, but no terminator (tests buffering / partial reads).
            (b"{\"context\":\"playerstatus\"}".to_vec(), "no-terminator")
        }
        _ => {
            // Embedded control bytes inside a string.
            let mut v = b"{\"context\":\"x\",\"data\":\"".to_vec();
            v.extend_from_slice(&[0x00, 0x01, 0x1f]);
            v.extend_from_slice(b"\"}\r\n");
            (v, "control-bytes")
        }
    };
    Input {
        bytes,
        note: format!("malformed {note}"),
    }
}

fn random_value(rng: &mut Rng, depth: u8) -> Value {
    if depth == 0 {
        return random_scalar(rng);
    }
    match rng.below(8) {
        5 => Value::Array(
            (0..rng.below(4))
                .map(|_| random_value(rng, depth - 1))
                .collect(),
        ),
        6 => {
            let mut m = Map::new();
            for _ in 0..rng.below(4) {
                m.insert(random_key(rng), random_value(rng, depth - 1));
            }
            Value::Object(m)
        }
        _ => random_scalar(rng),
    }
}

fn random_scalar(rng: &mut Rng) -> Value {
    match rng.below(9) {
        0 => Value::Null,
        1 => Value::Bool(rng.bool()),
        2 => Value::from(0),
        3 => Value::from(-1),
        4 => Value::from(i64::MAX),
        5 => Value::from(i64::MIN),
        6 => Value::from(""),
        7 => Value::from("A".repeat(1 + rng.below(500))),
        _ => Value::from(rng.next_u64()),
    }
}

fn random_key(rng: &mut Rng) -> String {
    const KEYS: &[&str] = &[
        "id",
        "index",
        "offset",
        "value",
        "type",
        "x",
        "data",
        "unexpected",
    ];
    (*rng.choice(KEYS)).to_string()
}

/// Mutate a value in place: drop a field, swap a type, or set an extreme.
fn mutate_value(rng: &mut Rng, v: &mut Value) {
    match v {
        Value::Object(o) if !o.is_empty() => {
            let keys: Vec<String> = o.keys().cloned().collect();
            let k = rng.choice(&keys).clone();
            match rng.below(3) {
                0 => {
                    o.remove(&k);
                }
                1 => {
                    o.insert(k, random_scalar(rng));
                }
                _ => {
                    o.insert(random_key(rng), random_scalar(rng));
                }
            }
        }
        _ => *v = random_scalar(rng),
    }
}

// -- Execution -------------------------------------------------------------

/// Drive the inputs at one target, returning the recorded capture lines and any
/// anomalies observed.
async fn drive(
    host: &str,
    port: u16,
    inputs: &[Input],
    wait: Duration,
) -> std::io::Result<(Vec<String>, Vec<String>)> {
    let stream = TcpStream::connect((host, port)).await?;
    stream.set_nodelay(true).ok();
    let (mut rd, mut wr) = stream.into_split();
    let mut acc = FrameAccumulator::default();
    let mut buf = vec![0u8; 8192];
    let mut lines: Vec<String> = Vec::new();
    let mut anomalies: Vec<String> = Vec::new();
    let mut seq: u64 = 0;

    // Handshake.
    let mut hs = ClientHandshake::new("Android", 4, false);
    write_and_record(&mut wr, &mut lines, &mut seq, "c2s", &hs.initial()).await?;
    let mut handshake_done = false;
    while !handshake_done {
        match tokio::time::timeout(wait, rd.read(&mut buf)).await {
            Ok(Ok(0)) | Err(_) => break,
            Ok(Ok(n)) => acc.push_bytes(&buf[..n]),
            Ok(Err(e)) => return Err(e),
        }
        while let Some(line) = acc.next_frame() {
            record(&mut lines, &mut seq, "s2c", &line);
            if let Some(reply) = hs.on_incoming(&parse_context(&line).unwrap_or_default()) {
                write_and_record(&mut wr, &mut lines, &mut seq, "c2s", &reply).await?;
                if parse_context(&line).as_deref() == Some(CTX_PLAYER) {
                    handshake_done = true;
                }
            }
        }
    }

    for (i, input) in inputs.iter().enumerate() {
        if wr.write_all(&input.bytes).await.is_err() {
            anomalies.push(format!("write failed at #{i} ({})", input.note));
            break;
        }
        wr.flush().await.ok();
        let raw = String::from_utf8_lossy(&input.bytes);
        record(
            &mut lines,
            &mut seq,
            "c2s",
            raw.trim_end_matches(['\r', '\n']),
        );

        // Drain responses for this input.
        let mut closed = false;
        loop {
            match tokio::time::timeout(wait, rd.read(&mut buf)).await {
                Ok(Ok(0)) => {
                    closed = true;
                    break;
                }
                Ok(Ok(n)) => acc.push_bytes(&buf[..n]),
                Ok(Err(_)) | Err(_) => break, // read error or idle
            }
            while let Some(line) = acc.next_frame() {
                if line.trim().is_empty() {
                    continue;
                }
                record(&mut lines, &mut seq, "s2c", &line);
                if parse_context(&line).is_none() {
                    anomalies.push(format!(
                        "non-JSON response to #{i} ({}): {}",
                        input.note,
                        snippet(&line)
                    ));
                }
                // Answer keepalive pings so the connection survives.
                if parse_context(&line).as_deref() == Some("ping") {
                    let _ = write_and_record(
                        &mut wr,
                        &mut lines,
                        &mut seq,
                        "c2s",
                        &mbrc_wire::pong_frame(),
                    )
                    .await;
                }
            }
        }
        if closed {
            anomalies.push(format!("connection closed after #{i} ({})", input.note));
            break;
        }

        // Liveness probe every 25 inputs.
        if (i + 1) % 25 == 0 && !probe(&mut wr, &mut rd, &mut acc, &mut buf, wait).await? {
            anomalies.push(format!(
                "liveness probe failed after #{i} - possible hang/desync"
            ));
            break;
        }
    }

    Ok((lines, anomalies))
}

/// Send `playerstatus` and confirm a well-formed `playerstatus` reply comes back.
async fn probe<R, W>(
    wr: &mut W,
    rd: &mut R,
    acc: &mut FrameAccumulator,
    buf: &mut [u8],
    wait: Duration,
) -> std::io::Result<bool>
where
    R: AsyncReadExt + Unpin,
    W: AsyncWrite + Unpin,
{
    wr.write_all(frame_line(r#"{"context":"playerstatus"}"#).as_bytes())
        .await?;
    wr.flush().await.ok();
    let deadline = tokio::time::Instant::now() + wait * 4;
    loop {
        let remaining = deadline.saturating_duration_since(tokio::time::Instant::now());
        if remaining.is_zero() {
            return Ok(false);
        }
        match tokio::time::timeout(remaining, rd.read(buf)).await {
            Ok(Ok(0)) | Err(_) => return Ok(false),
            Ok(Ok(n)) => acc.push_bytes(&buf[..n]),
            Ok(Err(e)) => return Err(e),
        }
        while let Some(line) = acc.next_frame() {
            if parse_context(&line).as_deref() == Some("playerstatus") {
                return Ok(true);
            }
        }
    }
}

async fn write_and_record<W: AsyncWrite + Unpin>(
    wr: &mut W,
    lines: &mut Vec<String>,
    seq: &mut u64,
    dir: &str,
    raw: &str,
) -> std::io::Result<()> {
    wr.write_all(frame_line(raw).as_bytes()).await?;
    wr.flush().await.ok();
    record(lines, seq, dir, raw);
    Ok(())
}

fn record(lines: &mut Vec<String>, seq: &mut u64, dir: &str, raw: &str) {
    let s = *seq;
    *seq += 1;
    let f = Frame::new(0, s, dir, 0, raw.as_bytes());
    lines.push(serde_json::to_string(&f).unwrap_or_default());
}

fn snippet(s: &str) -> String {
    if s.chars().count() > 80 {
        s.chars().take(80).collect::<String>() + "\u{2026}"
    } else {
        s.to_string()
    }
}

// -- Snapshot / restore (Phase 2, --destructive) ---------------------------

/// Reversible player state read from `playerstatus`.
#[derive(Debug)]
struct PlayerSnapshot {
    volume: Option<i64>,
    mute: Option<bool>,
    scrobbler: Option<bool>,
}

/// Connect and complete the V4 handshake, returning the split stream + the
/// accumulator (which may hold already-buffered post-handshake frames).
async fn handshake_conn(
    host: &str,
    port: u16,
    wait: Duration,
) -> std::io::Result<(
    tokio::net::tcp::OwnedReadHalf,
    tokio::net::tcp::OwnedWriteHalf,
    FrameAccumulator,
)> {
    let stream = TcpStream::connect((host, port)).await?;
    stream.set_nodelay(true).ok();
    let (mut rd, mut wr) = stream.into_split();
    let mut acc = FrameAccumulator::default();
    let mut buf = vec![0u8; 8192];
    let mut hs = ClientHandshake::new("Android", 4, false);
    wr.write_all(frame_line(&hs.initial()).as_bytes()).await?;
    wr.flush().await.ok();
    let mut done = false;
    while !done {
        match tokio::time::timeout(wait, rd.read(&mut buf)).await {
            Ok(Ok(0)) | Err(_) => break,
            Ok(Ok(n)) => acc.push_bytes(&buf[..n]),
            Ok(Err(e)) => return Err(e),
        }
        while let Some(line) = acc.next_frame() {
            let ctx = parse_context(&line).unwrap_or_default();
            if let Some(reply) = hs.on_incoming(&ctx) {
                wr.write_all(frame_line(&reply).as_bytes()).await?;
                wr.flush().await.ok();
                if ctx == CTX_PLAYER {
                    done = true;
                }
            }
        }
    }
    Ok((rd, wr, acc))
}

/// `playervolume` comes back as a *string* ("81") in V4 - accept int or string.
fn parse_int(v: &Value) -> Option<i64> {
    v.as_i64()
        .or_else(|| v.as_str().and_then(|s| s.parse().ok()))
}

/// Snapshot reversible player state via one `playerstatus`.
async fn snapshot_player(
    host: &str,
    port: u16,
    wait: Duration,
) -> std::io::Result<Option<PlayerSnapshot>> {
    let (mut rd, mut wr, mut acc) = handshake_conn(host, port, wait).await?;
    wr.write_all(frame_line(r#"{"context":"playerstatus"}"#).as_bytes())
        .await?;
    wr.flush().await.ok();
    let mut buf = vec![0u8; 8192];
    let deadline = tokio::time::Instant::now() + wait * 4;
    loop {
        let remaining = deadline.saturating_duration_since(tokio::time::Instant::now());
        if remaining.is_zero() {
            return Ok(None);
        }
        match tokio::time::timeout(remaining, rd.read(&mut buf)).await {
            Ok(Ok(0)) | Err(_) => return Ok(None),
            Ok(Ok(n)) => acc.push_bytes(&buf[..n]),
            Ok(Err(e)) => return Err(e),
        }
        while let Some(line) = acc.next_frame() {
            if parse_context(&line).as_deref() == Some("playerstatus") {
                let v = mbrc_wire::parse_lenient(&line).unwrap_or(Value::Null);
                let d = v.get("data");
                return Ok(Some(PlayerSnapshot {
                    volume: d.and_then(|d| d.get("playervolume")).and_then(parse_int),
                    mute: d.and_then(|d| d.get("playermute")).and_then(Value::as_bool),
                    scrobbler: d.and_then(|d| d.get("scrobbler")).and_then(Value::as_bool),
                }));
            }
        }
    }
}

/// Restore the snapshot with the matching set commands.
async fn restore_player(
    host: &str,
    port: u16,
    wait: Duration,
    snap: &PlayerSnapshot,
) -> std::io::Result<()> {
    let (mut rd, mut wr, _) = handshake_conn(host, port, wait).await?;
    let mut sets: Vec<String> = Vec::new();
    if let Some(v) = snap.volume {
        sets.push(format!(r#"{{"context":"playervolume","data":{v}}}"#));
    }
    if let Some(m) = snap.mute {
        sets.push(format!(r#"{{"context":"playermute","data":{m}}}"#));
    }
    if let Some(s) = snap.scrobbler {
        sets.push(format!(r#"{{"context":"scrobbler","data":{s}}}"#));
    }
    for s in &sets {
        wr.write_all(frame_line(s).as_bytes()).await?;
        wr.flush().await.ok();
    }
    // Brief drain so the sets are processed before close.
    let mut buf = vec![0u8; 4096];
    let _ = tokio::time::timeout(wait, rd.read(&mut buf)).await;
    Ok(())
}
