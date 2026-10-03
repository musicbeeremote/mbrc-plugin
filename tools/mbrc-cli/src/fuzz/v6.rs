//! The read-only fuzzer for the V6 envelope protocol.
//!
//! A harvest pass reads real genres, artists, albums, tracks, covers,
//! playlists and podcast episodes off the target first. Inputs are then about
//! half valid requests built from those values, a third valid requests with one
//! thing broken, and the rest malformed envelopes.
//!
//! Every reply is matched to its request by id. Anomalies: a request that gets
//! no answer, or two; a reply to an id never sent; a reply slower than
//! `--slow-ms`; a non-JSON reply; a dropped socket; and a valid request
//! answered with an error. A typed error to a broken request is the expected
//! answer and never an anomaly.

mod ops;

use std::collections::{BTreeMap, HashMap, HashSet};
use std::process::ExitCode;
use std::sync::Arc;
use std::time::Duration;

use mbrc_capture::Frame;
use mbrc_wire::FrameAccumulator;
use mbrc_wire::v6::{self, ClientType, IncomingResponse};
use serde_json::{Value, json};
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::TcpStream;
use tokio::net::tcp::{OwnedReadHalf, OwnedWriteHalf};
use tokio::time::Instant;

use super::{Input, MAX_BLOB, report_anomalies, save_script, snippet};
use crate::args::{flag_value, has_flag, run_client_id};
use crate::rng::Rng;
use ops::{Harvest, SPECS};

/// Ids at and above these belong to the harvest and the liveness probe, never to
/// a generated input.
const HARVEST_IDS: u64 = 1_000_000_000;
const PROBE_IDS: u64 = 2_000_000_000;

/// What a reply to one request has to be.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum Expect {
    /// A valid request: answered with success. `not_found` is allowed when it
    /// named a library item, which can have gone since the harvest.
    Success { item_may_be_gone: bool },
    /// A well-formed envelope: answered, with success or a typed error.
    Answer,
    /// Malformed, or glued to an unterminated frame before it: no promise.
    Nothing,
}

struct Request {
    input: Input,
    id: u64,
    op: &'static str,
    expect: Expect,
}

struct Settings {
    host: String,
    port: u16,
    seed: u64,
    iterations: usize,
    wait: Duration,
    slow: Duration,
    connections: u64,
    dribble: bool,
}

pub fn run(args: &[String], host: &str, port: u16) -> ExitCode {
    for flag in ["--destructive", "--diff-host", "--corpus"] {
        if has_flag(args, flag) {
            eprintln!("{flag} is not supported with --protocol 6");
            return ExitCode::FAILURE;
        }
    }
    let number = |flag: &str, default: u64| {
        flag_value(args, flag)
            .and_then(|s| s.parse().ok())
            .unwrap_or(default)
    };
    let settings = Arc::new(Settings {
        host: host.to_string(),
        port,
        seed: number("--seed", 1),
        iterations: number("--iterations", 200) as usize,
        // A browse op returns large pages off a real library.
        wait: Duration::from_millis(number("--wait-ms", 600)),
        slow: Duration::from_millis(number("--slow-ms", 2000)),
        connections: number("--connections", 1).max(1),
        dribble: has_flag(args, "--dribble"),
    });
    let runtime = match tokio::runtime::Runtime::new() {
        Ok(rt) => rt,
        Err(e) => {
            eprintln!("runtime init failed: {e}");
            return ExitCode::FAILURE;
        }
    };
    match runtime.block_on(fuzz(settings.clone(), flag_value(args, "--save-script"))) {
        Ok(run) => finish(&settings, run, flag_value(args, "--out")),
        Err(e) => {
            eprintln!("fuzz run failed: {e}");
            ExitCode::FAILURE
        }
    }
}

/// Everything the connections found, merged.
struct Run {
    harvest: String,
    lines: Vec<String>,
    anomalies: Vec<String>,
    coverage: BTreeMap<&'static str, BTreeMap<String, usize>>,
    skipped: BTreeMap<&'static str, usize>,
}

async fn fuzz(settings: Arc<Settings>, script: Option<String>) -> std::io::Result<Run> {
    let mut first = Conn::open(&settings, 0).await?;
    let mut harvest = Harvest::default();
    collect(&mut first, &mut harvest).await?;
    harvest.settle();
    let harvest = Arc::new(harvest);
    let mut first = Some(first);

    let mut skipped = BTreeMap::new();
    let mut plans: Vec<Vec<Request>> = (0..settings.connections)
        .map(|k| {
            generate(
                &mut Rng::new(settings.seed + k),
                settings.iterations,
                &harvest,
                &mut skipped,
            )
        })
        .collect();
    if let Some(path) = script {
        let inputs: Vec<Input> = plans[0]
            .iter()
            .map(|r| Input {
                bytes: r.input.bytes.clone(),
                note: r.input.note.clone(),
            })
            .collect();
        save_script(&path, &inputs);
    }

    let mut workers = Vec::new();
    for (k, plan) in plans.drain(..).enumerate() {
        let settings = settings.clone();
        let conn = first.take();
        workers.push(tokio::spawn(async move {
            let conn = match conn {
                Some(conn) => conn,
                None => Conn::open(&settings, k as u32).await?,
            };
            drive(conn, plan, &settings).await
        }));
    }

    let mut run = Run {
        harvest: harvest.summary(),
        lines: Vec::new(),
        anomalies: Vec::new(),
        coverage: BTreeMap::new(),
        skipped,
    };
    for (k, worker) in workers.into_iter().enumerate() {
        let tracker = worker
            .await
            .map_err(|e| std::io::Error::other(e.to_string()))??;
        run.lines.extend(tracker.lines);
        let label = |a: String| {
            if settings.connections > 1 {
                format!("conn {k}: {a}")
            } else {
                a
            }
        };
        run.anomalies
            .extend(tracker.anomalies.into_iter().map(label));
        for (op, outcomes) in tracker.coverage {
            let merged = run.coverage.entry(op).or_default();
            for (outcome, n) in outcomes {
                *merged.entry(outcome).or_default() += n;
            }
        }
    }
    Ok(run)
}

fn finish(settings: &Settings, run: Run, out: Option<String>) -> ExitCode {
    if let Some(path) = out {
        let _ = std::fs::write(path, run.lines.join("\n"));
    }
    println!(
        "fuzzed {} V6 input(s) on {} connection(s) against {}:{} (seed {}{})",
        settings.iterations as u64 * settings.connections,
        settings.connections,
        settings.host,
        settings.port,
        settings.seed,
        if settings.dribble { ", dribbled" } else { "" },
    );
    println!("harvested {}", run.harvest);
    print_coverage(&run);
    report_anomalies("target", &run.anomalies);
    println!(
        "\nseed {} reproduces this run against the same library.",
        settings.seed
    );
    if run.anomalies.is_empty() {
        ExitCode::SUCCESS
    } else {
        ExitCode::FAILURE
    }
}

fn print_coverage(run: &Run) {
    println!("\nper op (outcome counts):");
    for spec in SPECS {
        let outcomes = run.coverage.get(spec.op);
        let shown = outcomes.map_or_else(
            || "never sent".to_string(),
            |o| {
                o.iter()
                    .map(|(k, n)| format!("{k}={n}"))
                    .collect::<Vec<_>>()
                    .join(" ")
            },
        );
        let never_ok = outcomes.is_none_or(|o| !o.contains_key("ok"));
        let skipped = run.skipped.get(spec.op).map_or(String::new(), |n| {
            format!("  ({n} valid request(s) skipped: nothing harvested to name)")
        });
        let flag = if never_ok { "  <- never succeeded" } else { "" };
        println!("  {:<24} {shown}{flag}{skipped}", spec.op);
    }
}

/// Reads real values for the generators, a page from the start and one from the
/// middle of each list.
async fn collect(conn: &mut Conn, harvest: &mut Harvest) -> std::io::Result<()> {
    let mut id = HARVEST_IDS;
    for op in [
        "library_genres",
        "library_artists",
        "library_albums",
        "library_tracks",
    ] {
        for offset in [0, -1] {
            let total = if offset < 0 {
                let first = conn.ask(&mut id, op, json!({ "limit": 1 })).await?;
                first.get("total").and_then(Value::as_u64).unwrap_or(0) / 2
            } else {
                0
            };
            let page = conn
                .ask(&mut id, op, json!({ "offset": total, "limit": 100 }))
                .await?;
            for item in items(&page) {
                take(harvest, op, item);
            }
        }
    }
    for op in ["now_playing_list", "playlist_list", "podcast_subscriptions"] {
        let page = conn.ask(&mut id, op, json!({ "limit": 100 })).await?;
        for item in items(&page) {
            take(harvest, op, item);
        }
    }
    for podcast in harvest.podcasts.clone().iter().take(3) {
        let page = conn
            .ask(
                &mut id,
                "podcast_episodes",
                json!({ "id": podcast, "limit": 20 }),
            )
            .await?;
        for item in items(&page) {
            if let Some(index) = item.get("index").and_then(Value::as_i64) {
                harvest.episodes.push((podcast.clone(), index));
            }
        }
    }
    Ok(())
}

fn items(page: &Value) -> &[Value] {
    page.get("items")
        .and_then(Value::as_array)
        .map_or(&[], Vec::as_slice)
}

fn take(harvest: &mut Harvest, op: &str, item: &Value) {
    let text = |key: &str| item.get(key).and_then(Value::as_str).map(str::to_string);
    let add = |pool: &mut Vec<String>, key: &str| pool.extend(text(key));
    match op {
        "library_genres" => add(&mut harvest.genres, "genre"),
        "library_artists" => add(&mut harvest.artists, "artist"),
        "library_albums" => {
            if let (Some(album), Some(artist)) = (text("album"), text("artist")) {
                harvest.albums.push((album, artist));
            }
            add(&mut harvest.covers, "cover_hash");
        }
        "library_tracks" | "now_playing_list" => {
            add(&mut harvest.srcs, "src");
            add(&mut harvest.covers, "cover_hash");
        }
        "playlist_list" => add(&mut harvest.playlists, "url"),
        "podcast_subscriptions" => add(&mut harvest.podcasts, "id"),
        _ => {}
    }
}

fn generate(
    rng: &mut Rng,
    iterations: usize,
    harvest: &Harvest,
    skipped: &mut BTreeMap<&'static str, usize>,
) -> Vec<Request> {
    let mut unterminated = false;
    (1..=iterations as u64)
        .map(|id| {
            let mut request = pick(rng, id, harvest, skipped);
            if unterminated {
                request.expect = Expect::Nothing;
            }
            unterminated = !request.input.bytes.ends_with(b"\n");
            request
        })
        .collect()
}

fn pick(
    rng: &mut Rng,
    id: u64,
    harvest: &Harvest,
    skipped: &mut BTreeMap<&'static str, usize>,
) -> Request {
    let roll = rng.below(10);
    if roll >= 8 {
        return malformed(rng, id);
    }
    let spec = rng.choice(SPECS);
    let Some((data, item_may_be_gone)) = ops::valid(rng, spec, harvest) else {
        *skipped.entry(spec.op).or_default() += 1;
        return malformed(rng, id);
    };
    let (data, note, expect) = if roll < 5 {
        (
            data,
            "valid".to_string(),
            Expect::Success { item_may_be_gone },
        )
    } else {
        let (data, broke) = ops::mutate(rng, spec, data);
        (data, broke.to_string(), Expect::Answer)
    };
    Request {
        input: Input {
            bytes: v6::frame_line(&v6::request(id, spec.op, data)).into_bytes(),
            note: format!("{note} {}", spec.op),
        },
        id,
        op: spec.op,
        expect,
    }
}

/// A frame that breaks the envelope contract. Answered or not, it must never
/// cost the connection.
fn malformed(rng: &mut Rng, id: u64) -> Request {
    let request = |op: &str, data: &str| {
        format!("{{\"id\":{id},\"kind\":\"request\",\"op\":\"{op}\",\"data\":{data}}}\n")
    };
    let (text, note): (String, &str) = match rng.below(10) {
        0 => ("not json at all\n".into(), "non-json"),
        1 => (
            format!("{{\"id\":{id},\"op\":\"player_status\",\"data\":{{}}}}\n"),
            "missing-kind",
        ),
        2 => (
            format!("{{\"id\":{id},\"kind\":\"frobnicate\",\"op\":\"x\",\"data\":{{}}}}\n"),
            "bad-kind",
        ),
        3 => (
            format!("{{\"id\":{id},\"kind\":\"request\",\"data\":{{}}}}\n"),
            "missing-op",
        ),
        4 => (request("totally_unknown_op", "{}"), "unknown-op"),
        5 => ("[]\n".into(), "array-not-object"),
        6 => (
            request(
                "player_status",
                &format!("\"{}\"", "A".repeat(1 + rng.below(MAX_BLOB))),
            ),
            "oversized",
        ),
        7 => (
            request("player_status", "{}").trim_end().to_string(),
            "no-terminator",
        ),
        8 => (
            format!(
                "{{\"id\":-{id},\"kind\":\"request\",\"op\":\"player_status\",\"data\":{{}}}}\n"
            ),
            "negative-id",
        ),
        _ => (
            request("track_get", "{\"src\":\"\u{0}\u{1}\u{1f}\"}"),
            "control-bytes",
        ),
    };
    Request {
        input: Input {
            bytes: text.into_bytes(),
            note: format!("malformed {note}"),
        },
        id,
        op: "malformed",
        expect: Expect::Nothing,
    }
}

/// One V6 connection, past its handshake.
struct Conn {
    rd: OwnedReadHalf,
    wr: OwnedWriteHalf,
    acc: FrameAccumulator,
    buf: Vec<u8>,
    lines: Vec<String>,
    seq: u64,
    conn_id: u32,
    dribble: Option<Rng>,
    wait: Duration,
}

impl Conn {
    async fn open(settings: &Settings, conn_id: u32) -> std::io::Result<Self> {
        let stream = TcpStream::connect((settings.host.as_str(), settings.port)).await?;
        stream.set_nodelay(true).ok();
        let (rd, wr) = stream.into_split();
        let mut conn = Self {
            rd,
            wr,
            acc: FrameAccumulator::default(),
            buf: vec![0u8; 8192],
            lines: Vec::new(),
            seq: 0,
            conn_id,
            dribble: settings
                .dribble
                .then(|| Rng::new(settings.seed ^ 0xD1B_B1E ^ u64::from(conn_id))),
            wait: settings.wait,
        };
        let hello = v6::handshake_request(&run_client_id("mbrc-fuzz"), ClientType::Cli, true);
        conn.write(v6::frame_line(&hello).as_bytes()).await?;
        let deadline = Instant::now() + settings.wait * 8;
        while Instant::now() < deadline {
            for line in conn.read(settings.wait).await?.unwrap_or_default() {
                match v6::parse_response(&line) {
                    Some(IncomingResponse {
                        id: 0,
                        result: Ok(_),
                    }) => return Ok(conn),
                    Some(IncomingResponse {
                        id: 0,
                        result: Err(e),
                    }) => {
                        return Err(std::io::Error::other(format!(
                            "handshake rejected: {} - {}",
                            e.code, e.message
                        )));
                    }
                    _ => {}
                }
            }
        }
        Err(std::io::Error::other("handshake did not complete"))
    }

    async fn write(&mut self, bytes: &[u8]) -> std::io::Result<()> {
        let raw = String::from_utf8_lossy(bytes);
        self.record("c2s", raw.trim_end_matches(['\r', '\n']));
        match &mut self.dribble {
            None => self.wr.write_all(bytes).await?,
            Some(rng) => {
                let mut rest = bytes;
                while !rest.is_empty() {
                    let n = 1 + rng.below(rest.len().min(64));
                    self.wr.write_all(&rest[..n]).await?;
                    rest = &rest[n..];
                    let pause = rng.below(21) as u64;
                    tokio::time::sleep(Duration::from_millis(pause)).await;
                }
            }
        }
        self.wr.flush().await
    }

    /// Whole lines that arrived within `wait`; `None` once the server closed.
    async fn read(&mut self, wait: Duration) -> std::io::Result<Option<Vec<String>>> {
        match tokio::time::timeout(wait, self.rd.read(&mut self.buf)).await {
            Ok(Ok(0)) => return Ok(None),
            Ok(Ok(n)) => self.acc.push_bytes(&self.buf[..n]),
            Ok(Err(e)) => return Err(e),
            Err(_) => {}
        }
        let mut lines = Vec::new();
        while let Some(line) = self.acc.next_frame() {
            if !line.trim().is_empty() {
                self.record("s2c", &line);
                lines.push(line);
            }
        }
        Ok(Some(lines))
    }

    /// One harvest request and its successful answer, or an empty object.
    async fn ask(&mut self, id: &mut u64, op: &str, data: Value) -> std::io::Result<Value> {
        *id += 1;
        let want = *id;
        self.write(v6::frame_line(&v6::request(want, op, data)).as_bytes())
            .await?;
        let deadline = Instant::now() + self.wait * 8;
        while Instant::now() < deadline {
            let Some(lines) = self.read(self.wait).await? else {
                return Err(std::io::Error::other("connection closed during harvest"));
            };
            for line in lines {
                if let Some(r) = v6::parse_response(&line).filter(|r| r.id == want) {
                    return Ok(r.result.unwrap_or(Value::Null));
                }
            }
        }
        Ok(Value::Null)
    }

    fn record(&mut self, dir: &str, raw: &str) {
        let frame = Frame::new(self.conn_id, self.seq, dir, 0, raw.as_bytes());
        self.seq += 1;
        self.lines
            .push(serde_json::to_string(&frame).unwrap_or_default());
    }
}

/// Matches replies to requests and keeps the score.
#[derive(Default)]
struct Tracker {
    pending: HashMap<u64, (Instant, &'static str, Expect, String)>,
    sent: HashSet<u64>,
    answered: HashSet<u64>,
    anomalies: Vec<String>,
    coverage: BTreeMap<&'static str, BTreeMap<String, usize>>,
    lines: Vec<String>,
}

impl Tracker {
    fn sent(&mut self, id: u64, op: &'static str, expect: Expect, note: &str) {
        self.sent.insert(id);
        self.pending
            .insert(id, (Instant::now(), op, expect, note.to_string()));
    }

    fn reply(&mut self, line: &str, slow: Duration) {
        if serde_json::from_str::<Value>(line)
            .ok()
            .filter(Value::is_object)
            .is_none()
        {
            self.anomalies
                .push(format!("non-JSON reply: {}", snippet(line)));
            return;
        }
        let Some(response) = v6::parse_response(line) else {
            return;
        };
        let id = response.id;
        if id == 0 {
            return;
        }
        if !self.answered.insert(id) {
            self.anomalies
                .push(format!("id {id} answered twice: {}", snippet(line)));
            return;
        }
        let Some((sent_at, op, expect, note)) = self.pending.remove(&id) else {
            if !self.sent.contains(&id) {
                self.anomalies
                    .push(format!("reply to an id never sent: {}", snippet(line)));
            }
            return;
        };
        let took = sent_at.elapsed();
        if took > slow {
            self.anomalies
                .push(format!("#{id} ({note}) took {} ms", took.as_millis()));
        }
        let outcome = match &response.result {
            Ok(_) => "ok".to_string(),
            Err(e) => e.code.clone(),
        };
        if op != "malformed" && id < HARVEST_IDS {
            *self
                .coverage
                .entry(op)
                .or_default()
                .entry(outcome.clone())
                .or_default() += 1;
        }
        if let (Expect::Success { item_may_be_gone }, Err(e)) = (expect, &response.result) {
            let gone = item_may_be_gone && e.code == "not_found";
            if !gone {
                self.anomalies.push(format!(
                    "valid request rejected: #{id} {note}: {} {}{}",
                    e.code,
                    e.message,
                    e.field
                        .as_deref()
                        .map(|f| format!(" (field {f})"))
                        .unwrap_or_default(),
                ));
            }
        }
    }

    fn unanswered(&self, id: u64) -> bool {
        self.pending.contains_key(&id)
    }
}

async fn drive(
    mut conn: Conn,
    plan: Vec<Request>,
    settings: &Settings,
) -> std::io::Result<Tracker> {
    let mut tracker = Tracker::default();
    let mut probe_id = PROBE_IDS;
    for (i, request) in plan.iter().enumerate() {
        if conn.write(&request.input.bytes).await.is_err() {
            tracker.anomalies.push(format!(
                "write failed at #{} ({})",
                request.id, request.input.note
            ));
            break;
        }
        tracker.sent(request.id, request.op, request.expect, &request.input.note);
        if !settle(&mut conn, &mut tracker, request, settings).await? {
            break;
        }
        if (i + 1) % 25 == 0 {
            probe_id += 1;
            let probe = Request {
                input: Input {
                    bytes: v6::frame_line(&v6::request(probe_id, "player_status", json!({})))
                        .into_bytes(),
                    note: "liveness probe".into(),
                },
                id: probe_id,
                op: "player_status",
                expect: Expect::Success {
                    item_may_be_gone: false,
                },
            };
            if !request.input.bytes.ends_with(b"\n") {
                // Closes the open frame so the probe is not glued onto it.
                conn.write(b"\n").await?;
            }
            conn.write(&probe.input.bytes).await?;
            tracker.sent(probe.id, probe.op, probe.expect, &probe.input.note);
            if !settle(&mut conn, &mut tracker, &probe, settings).await? {
                break;
            }
        }
    }
    tracker.lines = std::mem::take(&mut conn.lines);
    Ok(tracker)
}

/// Reads replies until this request is answered, or for one idle window when
/// it promises nothing. Returns false once the connection is gone.
async fn settle(
    conn: &mut Conn,
    tracker: &mut Tracker,
    request: &Request,
    settings: &Settings,
) -> std::io::Result<bool> {
    let must_answer = request.expect != Expect::Nothing;
    let deadline = Instant::now() + settings.slow * 3;
    loop {
        let Some(lines) = conn.read(settings.wait).await? else {
            tracker.anomalies.push(format!(
                "connection closed after #{} ({})",
                request.id, request.input.note
            ));
            return Ok(false);
        };
        let idle = lines.is_empty();
        for line in lines {
            tracker.reply(&line, settings.slow);
        }
        if must_answer && !tracker.unanswered(request.id) {
            return Ok(true);
        }
        if !must_answer && idle {
            return Ok(true);
        }
        if Instant::now() >= deadline {
            tracker.anomalies.push(format!(
                "no answer to #{} ({}) within {} ms",
                request.id,
                request.input.note,
                (settings.slow * 3).as_millis()
            ));
            return Ok(true);
        }
    }
}
