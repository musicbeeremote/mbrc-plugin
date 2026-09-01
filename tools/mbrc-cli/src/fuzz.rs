//! `mbrc fuzz` - a seeded protocol fuzzer for a V4 server.
//!
//! Generates a deterministic sequence of inputs from `--seed` and fires them at
//! a target, recording every input/response to an `mbrc-capture/2` trace and
//! flagging anomalies (a dropped connection, a non-JSON reply, a failed liveness
//! probe). It mixes three generators:
//!   * **structural** - a known context with edge-case / wrong-type `data`;
//!   * **malformed**  - invalid JSON, bad framing, oversized, iOS quirks;
//!   * **mutation**   - `--corpus <golden>` frames with their `data` mutated.
//!
//! **Read-only by default**: structural and mutation inputs only target
//! query-shaped contexts, so it is safe against a real library. `--destructive`
//! adds the reversible player state, snapshotted then restored. `--diff-host H2`
//! diffs against a second target, `--save-script F` freezes the inputs as a
//! replayable c2s script, `--protocol 6` selects the read-only V6 path.
//!
//! Usage:
//!   mbrc fuzz [--host H] [--port P] [--seed N] [--iterations K] [--wait-ms N]
//!             [--corpus <file|dir>] [--out F] [--diff-host H2] [--diff-port P2]
//!             [--destructive] [--save-script F] [--protocol 4|6]

use std::process::ExitCode;
use std::time::Duration;

use mbrc_capture::{Frame, Record, parse_line};
use mbrc_wire::{CTX_PLAYER, ClientHandshake, FrameAccumulator, frame_line, parse_context};
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
/// library and fingerprint exist.
const REVERSIBLE_CONTEXTS: &[&str] = &["playervolume", "playermute", "scrobbler"];

/// Cap oversized payloads so we stress buffers without risking an OOM on the
/// host.
const MAX_BLOB: usize = 50_000;

/// One generated input: the exact bytes to send plus a human note for reports.
struct Input {
    bytes: Vec<u8>,
    note: String,
}

/// Everything the fuzz subcommand takes off the command line.
struct Options {
    host: String,
    port: u16,
    seed: u64,
    iterations: usize,
    wait: Duration,
    destructive: bool,
    out_path: Option<String>,
    save_script: Option<String>,
    corpus: Option<String>,
    diff_host: Option<String>,
    diff_port: u16,
}

impl Options {
    fn parse(args: &[String]) -> Self {
        let port = parse_port(flag_value(args, "--port"), 3000);
        Self {
            host: flag_value(args, "--host").unwrap_or_else(|| "127.0.0.1".to_string()),
            port,
            seed: flag_value(args, "--seed")
                .and_then(|s| s.parse().ok())
                .unwrap_or(1),
            iterations: flag_value(args, "--iterations")
                .and_then(|s| s.parse().ok())
                .unwrap_or(200),
            wait: Duration::from_millis(
                flag_value(args, "--wait-ms")
                    .and_then(|s| s.parse().ok())
                    .unwrap_or(300),
            ),
            destructive: has_flag(args, "--destructive"),
            out_path: flag_value(args, "--out"),
            save_script: flag_value(args, "--save-script"),
            corpus: flag_value(args, "--corpus"),
            diff_host: flag_value(args, "--diff-host"),
            diff_port: parse_port(flag_value(args, "--diff-port"), port),
        }
    }
}

/// Freezes the generated inputs as a replayable c2s script.
///
/// Timestamps are pinned rather than taken from the clock, so a given seed
/// writes a byte-identical file.
fn save_script(path: &str, inputs: &[Input]) {
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
            f.ts = "1970-01-01T00:00:00Z".to_string();
            serde_json::to_string(&f).unwrap_or_default()
        })
        .collect();
    let _ = std::fs::write(path, script.join("\n"));
    println!("wrote {} input frames to {path}", inputs.len());
}

/// Reports both targets and returns whether the run should fail the process.
fn report_run(
    opts: &Options,
    inputs: &[Input],
    lines_a: &[String],
    anomalies_a: &[String],
    b: Option<(Vec<String>, Vec<String>)>,
) -> bool {
    let (host, port, seed) = (&opts.host, opts.port, opts.seed);
    println!(
        "fuzzed {} input(s) against {host}:{port} (seed {seed})",
        inputs.len()
    );
    report_anomalies("target A", anomalies_a);
    let mut failed = !anomalies_a.is_empty();

    if let Some((lines_b, anomalies_b)) = b {
        report_anomalies("target B", &anomalies_b);
        println!("\n-- differential diff (A vs B) --");
        // Drop ping/pong keepalives - their presence is timing-dependent, not a
        // real behavioral divergence between the two targets.
        let differing = diff_report(
            &strip_keepalive(lines_a),
            &strip_keepalive(&lines_b),
            false,
            &[],
        );
        failed = failed || !anomalies_b.is_empty() || differing > 0;
    }

    println!("\nseed {seed} reproduces this run.");
    failed
}

pub fn run(args: &[String]) -> ExitCode {
    let protocol: u8 = flag_value(args, "--protocol")
        .and_then(|s| s.parse().ok())
        .unwrap_or(4);
    // V6 has its own read-only path: no corpus mutation and no destructive player
    // state, because it has no snapshot/restore surface to reverse them with.
    if protocol == mbrc_wire::v6::PROTOCOL_VERSION as u8 {
        let host = flag_value(args, "--host").unwrap_or_else(|| "127.0.0.1".to_string());
        return v6::run(args, &host, parse_port(flag_value(args, "--port"), 3000));
    }
    let opts = Options::parse(args);

    if opts.destructive {
        eprintln!(
            "--destructive: fuzzing reversible player state ({}); snapshotting via playerstatus \
             and restoring afterward.",
            REVERSIBLE_CONTEXTS.join(", ")
        );
    }

    let corpus = opts
        .corpus
        .as_deref()
        .and_then(|p| read_all(p).ok())
        .map(|c| load_corpus(&c))
        .unwrap_or_default();

    let mut rng = Rng::new(opts.seed);
    let inputs = generate(&mut rng, opts.iterations, &corpus, opts.destructive);

    if let Some(path) = &opts.save_script {
        save_script(path, &inputs);
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
        let snapshot = if opts.destructive {
            snapshot_player(&opts.host, opts.port, opts.wait).await?
        } else {
            None
        };
        let a = drive(&opts.host, opts.port, &inputs, opts.wait).await?;
        let b = match &opts.diff_host {
            Some(h) => Some(drive(h, opts.diff_port, &inputs, opts.wait).await?),
            None => None,
        };
        if let Some(snap) = &snapshot {
            restore_player(&opts.host, opts.port, opts.wait, snap).await?;
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
    if let Some(path) = &opts.out_path {
        let _ = std::fs::write(path, lines_a.join("\n"));
    }

    if report_run(&opts, &inputs, &lines_a, &anomalies_a, b) {
        ExitCode::FAILURE
    } else {
        ExitCode::SUCCESS
    }
}

fn parse_port(v: Option<String>, default: u16) -> u16 {
    v.and_then(|s| s.parse().ok()).unwrap_or(default)
}

/// Drops ping/pong keepalive frames (timing noise) before a differential diff.
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

/// Mutates a value in place: drop a field, swap a type, or set an extreme.
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

/// Drives the inputs at one target, returning the recorded capture lines and any
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

/// Sends `playerstatus` and confirm a well-formed `playerstatus` reply comes back.
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

// -- Snapshot / restore (--destructive) ------------------------------------

/// Reversible player state read from `playerstatus`.
#[derive(Debug)]
struct PlayerSnapshot {
    volume: Option<i64>,
    mute: Option<bool>,
    scrobbler: Option<bool>,
}

/// Connects and complete the V4 handshake, returning the split stream + the
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

/// Snapshots reversible player state via one `playerstatus`.
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

/// Restores the snapshot with the matching set commands.
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

/// A self-contained read-only fuzzer for the V6 envelope protocol.
///
/// Structural and malformed generators only: there is no V6 golden corpus to
/// mutate, and no snapshot/restore surface for `--destructive`. The invariant
/// under test is that after the handshake the server answers EVERY frame,
/// however malformed, with a valid JSON envelope and keeps the connection. A
/// typed `error` response is the expected outcome for bad input and never an
/// anomaly; a dropped socket or a non-JSON reply is.
mod v6 {
    use std::process::ExitCode;
    use std::time::Duration;

    use mbrc_capture::Frame;
    use mbrc_wire::FrameAccumulator;
    use mbrc_wire::v6::{self, ClientType};
    use serde_json::Value;
    use tokio::io::{AsyncReadExt, AsyncWriteExt};
    use tokio::net::TcpStream;

    use super::{Input, MAX_BLOB, random_value, report_anomalies, snippet};
    use crate::args::{flag_value, has_flag};
    use crate::rng::Rng;

    /// Query-shaped V6 ops: getters / browses that never mutate library or
    /// player state. Setters, transport nav, queue/list mutations, play-all and
    /// seek are all excluded (they change state).
    const READONLY_OPS: &[&str] = &[
        "system_info",
        "player_status",
        "player_output",
        "now_playing_state",
        "now_playing_details",
        "now_playing_position",
        "now_playing_lyrics",
        "now_playing_list",
        "playlist_list",
        "library_genres",
        "library_artists",
        "library_albums",
        "library_tracks",
        "library_radio",
        "track_get",
        "cover_get",
    ];

    pub fn run(args: &[String], host: &str, port: u16) -> ExitCode {
        if has_flag(args, "--destructive") {
            eprintln!(
                "--destructive is not supported for --protocol 6 (read-only only); ignoring."
            );
        }
        let seed: u64 = flag_value(args, "--seed")
            .and_then(|s| s.parse().ok())
            .unwrap_or(1);
        let iterations: usize = flag_value(args, "--iterations")
            .and_then(|s| s.parse().ok())
            .unwrap_or(200);
        // Higher than the V4 default: a browse op returns large pages off a real
        // library, and a tight drain window reads as a false desync.
        let wait = Duration::from_millis(
            flag_value(args, "--wait-ms")
                .and_then(|s| s.parse().ok())
                .unwrap_or(600),
        );
        let out_path = flag_value(args, "--out");

        let mut rng = Rng::new(seed);
        let inputs = generate(&mut rng, iterations);

        let rt = match tokio::runtime::Runtime::new() {
            Ok(rt) => rt,
            Err(e) => {
                eprintln!("runtime init failed: {e}");
                return ExitCode::FAILURE;
            }
        };

        let result = rt.block_on(drive(host, port, &inputs, wait));
        let (lines, anomalies) = match result {
            Ok(v) => v,
            Err(e) => {
                eprintln!("fuzz run failed: {e}");
                return ExitCode::FAILURE;
            }
        };

        if let Some(path) = &out_path {
            let _ = std::fs::write(path, lines.join("\n"));
        }

        println!(
            "fuzzed {} V6 input(s) against {host}:{port} (seed {seed})",
            inputs.len()
        );
        report_anomalies("target", &anomalies);
        println!("\nseed {seed} reproduces this run.");
        if anomalies.is_empty() {
            ExitCode::SUCCESS
        } else {
            ExitCode::FAILURE
        }
    }

    fn generate(rng: &mut Rng, iterations: usize) -> Vec<Input> {
        (0..iterations)
            .map(|i| {
                // ~50% structural (known op, edge-case data), ~50% malformed.
                if rng.bool() {
                    structural(rng, i as u64)
                } else {
                    malformed(rng, i as u64)
                }
            })
            .collect()
    }

    /// A known read-only op with a random (often wrong-typed) `data` payload.
    fn structural(rng: &mut Rng, id: u64) -> Input {
        let pool = READONLY_OPS.to_vec();
        let op = *rng.choice(&pool);
        let data = random_value(rng, 3);
        Input {
            bytes: v6::frame_line(&v6::request(id, op, data)).into_bytes(),
            note: format!("structural {op}"),
        }
    }

    /// A frame that violates the envelope contract in some way. Newline-framed
    /// (V6), unlike the V4 generator's CRLF.
    fn malformed(rng: &mut Rng, id: u64) -> Input {
        let (bytes, note): (Vec<u8>, &str) = match rng.below(11) {
            0 => (b"not json at all\n".to_vec(), "non-json"),
            1 => (
                format!("{{\"id\":{id},\"op\":\"player_status\",\"data\":{{}}}}\n").into_bytes(),
                "missing-kind",
            ),
            2 => (
                format!("{{\"id\":{id},\"kind\":\"frobnicate\",\"op\":\"x\",\"data\":{{}}}}\n")
                    .into_bytes(),
                "bad-kind",
            ),
            3 => (
                format!("{{\"id\":{id},\"kind\":\"request\",\"data\":{{}}}}\n").into_bytes(),
                "missing-op",
            ),
            4 => (
                format!(
                    "{{\"id\":{id},\"kind\":\"request\",\"op\":\"totally_unknown_op\",\"data\":{{}}}}\n"
                )
                .into_bytes(),
                "unknown-op",
            ),
            5 => (
                format!(
                    "{{\"id\":{id},\"kind\":\"request\",\"op\":\"library_tracks\",\"data\":{{\"offset\":\"NaN\",\"limit\":[1,2]}}}}\n"
                )
                .into_bytes(),
                "wrong-typed-fields",
            ),
            6 => (b"[]\n".to_vec(), "array-not-object"),
            7 => (
                format!("{{\"id\":{id},\"kind\":\"request\",\"op\":\"track_get\",\"data\":{{}}}}\n")
                    .into_bytes(),
                "missing-required-field",
            ),
            8 => {
                // Oversized string payload.
                let blob = "A".repeat(1 + rng.below(MAX_BLOB));
                (
                    format!(
                        "{{\"id\":{id},\"kind\":\"request\",\"op\":\"player_status\",\"data\":\"{blob}\"}}\n"
                    )
                    .into_bytes(),
                    "oversized",
                )
            }
            9 => (
                // Valid frame, no terminator (buffering / partial-read stress).
                format!("{{\"id\":{id},\"kind\":\"request\",\"op\":\"player_status\",\"data\":{{}}}}")
                    .into_bytes(),
                "no-terminator",
            ),
            _ => {
                // Embedded control bytes inside a string value.
                let mut v = format!(
                    "{{\"id\":{id},\"kind\":\"request\",\"op\":\"track_get\",\"data\":{{\"src\":\""
                )
                .into_bytes();
                v.extend_from_slice(&[0x00, 0x01, 0x1f]);
                v.extend_from_slice(b"\"}}\n");
                (v, "control-bytes")
            }
        };
        Input {
            bytes,
            note: format!("malformed {note}"),
        }
    }

    fn record(lines: &mut Vec<String>, seq: &mut u64, dir: &str, raw: &str) {
        let s = *seq;
        *seq += 1;
        let f = Frame::new(0, s, dir, 0, raw.as_bytes());
        lines.push(serde_json::to_string(&f).unwrap_or_default());
    }

    /// Complete the V6 handshake (auto request + id:0 response), then fire every
    /// input and drain replies. Anomalies: dropped socket, or a non-JSON reply.
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

        // Handshake: quiet channel (no_broadcast) so only our replies come back.
        let hs = v6::handshake_request("mbrc-fuzz", ClientType::Cli, true);
        wr.write_all(v6::frame_line(&hs).as_bytes()).await?;
        wr.flush().await.ok();
        record(&mut lines, &mut seq, "c2s", &hs);
        let mut handshaked = false;
        let deadline = tokio::time::Instant::now() + wait * 8;
        while !handshaked {
            if tokio::time::Instant::now() >= deadline {
                anomalies.push("handshake did not complete".to_string());
                return Ok((lines, anomalies));
            }
            match tokio::time::timeout(wait, rd.read(&mut buf)).await {
                Ok(Ok(0)) => {
                    anomalies.push("connection closed during handshake".to_string());
                    return Ok((lines, anomalies));
                }
                Ok(Ok(n)) => acc.push_bytes(&buf[..n]),
                Ok(Err(e)) => return Err(e),
                Err(_) => continue,
            }
            while let Some(line) = acc.next_frame() {
                record(&mut lines, &mut seq, "s2c", &line);
                if matches!(v6::parse_response(&line), Some(r) if r.id == 0) {
                    handshaked = true;
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
                    // A reply must be a well-formed JSON object. Typed errors are
                    // expected (not anomalies); only unparseable JSON is bad.
                    if serde_json::from_str::<Value>(&line)
                        .ok()
                        .filter(Value::is_object)
                        .is_none()
                    {
                        anomalies.push(format!(
                            "non-JSON response to #{i} ({}): {}",
                            input.note,
                            snippet(&line)
                        ));
                    }
                }
            }
            if closed {
                anomalies.push(format!(
                    "connection closed after #{i} ({}) - V6 must reply, not drop, post-handshake",
                    input.note
                ));
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

    /// Send `player_status` and confirm a well-formed response returns.
    async fn probe(
        wr: &mut tokio::net::tcp::OwnedWriteHalf,
        rd: &mut tokio::net::tcp::OwnedReadHalf,
        acc: &mut FrameAccumulator,
        buf: &mut [u8],
        wait: Duration,
    ) -> std::io::Result<bool> {
        let probe = v6::request(999_999, "player_status", Value::Object(Default::default()));
        wr.write_all(v6::frame_line(&probe).as_bytes()).await?;
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
                if matches!(v6::parse_response(&line), Some(r) if r.id == 999_999) {
                    return Ok(true);
                }
            }
        }
    }
}
