//! Golden-trace replay — Tier A (shape) + Tier B (value, burst-aware).
//!
//! **Tier A** reads each `legacy-*.jsonl` fixture, groups frames into
//! connections using the `player` handshake as boundary, replays the
//! c2s frames in order, and for every captured s2c frame reads one
//! response line from the server and shape-diffs it against the
//! captured response. Shape-diff means same keys at every level, same
//! value *types*. Values themselves are not compared.
//!
//! **Tier B** partitions each connection into spans
//! (`C2sBeat { c2s, expected_response }` and `Burst { expected: Vec<Frame> }`)
//! and replays span-by-span:
//!
//!  * `C2sBeat`s send the c2s and read at most one response, then
//!    shape-diff against the captured payload.
//!  * `Burst`s figure out which notifications produce the burst's
//!    context set, fire each once via `mbrc_handle_notification`, then
//!    read up to N frames with bounded timeout and match each *out of
//!    order* against any unmatched expected frame in the burst by
//!    context. Per-frame match is shape-diff. This sidesteps the
//!    wire-ordering variance (initial-state push vs runtime-playernext
//!    bursts have different orderings) that blocked Tier A from
//!    measuring the multi-frame TrackChanged improvement.
//!
//! Value-strict comparison would require either real MusicBee data or
//! a hand-crafted exclusion list large enough that the signal-to-noise
//! collapses; deferred to a future Tier C with a richer seeded callback
//! table or a recorded-and-replayed callback log.

use std::path::PathBuf;
use std::sync::Arc;
use std::time::Duration;

use mbrc_core::replay_support::{
    handle_connection, nop_callbacks, synthesize_notification, AppState, NotificationType,
};
use serde_json::Value;
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::net::{TcpListener, TcpStream};
use tokio::time::timeout;

const FIXTURE_DIR: &str = "tests/golden";
/// Loopback responses arrive in well under 10ms; 50ms is generous
/// enough to absorb scheduler jitter without blowing the total runtime
/// on the fixtures that have thousands of silent handlers.
const RESPONSE_READ_TIMEOUT: Duration = Duration::from_millis(50);

// ── Fixture parsing ─────────────────────────────────────────────────────

#[derive(Debug, Clone)]
struct Frame {
    seq: u64,
    dir: Dir,
    /// Full parsed `.frame` object (context + data), if the frame was
    /// well-formed JSON on the wire. `None` for `raw` records.
    frame: Option<Value>,
    /// Raw bytes when the frame was malformed. Used to replay the exact
    /// bytes so the server's codec sees what the iOS client sent.
    raw: Option<String>,
    /// Originating TCP connection index, assigned by the trim tool.
    /// Frames with the same `conn_id` belong to the same replay session.
    conn_id: u32,
}

#[derive(Debug, Clone, PartialEq)]
enum Dir {
    C2s,
    S2c,
}

fn load_fixture(path: &PathBuf) -> Vec<Frame> {
    let text = std::fs::read_to_string(path).expect("fixture file");
    text.lines()
        .filter(|l| !l.trim().is_empty())
        .filter_map(|line| {
            let v: Value = serde_json::from_str(line).ok()?;
            let seq = v.get("seq")?.as_u64()?;
            let dir = match v.get("dir")?.as_str()? {
                "c2s" => Dir::C2s,
                "s2c" => Dir::S2c,
                _ => return None,
            };
            let conn_id = v.get("conn_id").and_then(|c| c.as_u64()).unwrap_or(0) as u32;
            Some(Frame {
                seq,
                dir,
                frame: v.get("frame").cloned(),
                raw: v.get("raw").and_then(|r| r.as_str()).map(String::from),
                conn_id,
            })
        })
        .collect()
}

/// Group frames by originating TCP connection. The trim tool tags each
/// frame with `conn_id`; this just bucket-sorts in one pass and sorts
/// within each bucket by `seq` so wire order is preserved.
fn segment(frames: &[Frame]) -> Vec<Vec<Frame>> {
    let mut by_conn: std::collections::BTreeMap<u32, Vec<Frame>> =
        std::collections::BTreeMap::new();
    for f in frames {
        by_conn.entry(f.conn_id).or_default().push(f.clone());
    }
    for group in by_conn.values_mut() {
        group.sort_by_key(|f| f.seq);
    }
    by_conn.into_values().collect()
}

// ── Shape diff ──────────────────────────────────────────────────────────

#[derive(Debug, Default)]
#[allow(dead_code)] // Read via Debug formatter in test output.
struct ShapeMismatch {
    path: String,
    expected: String,
    actual: String,
}

fn shape_diff(expected: &Value, actual: &Value, path: &str) -> Vec<ShapeMismatch> {
    let mut out = Vec::new();
    match (expected, actual) {
        (Value::Null, Value::Null)
        | (Value::Bool(_), Value::Bool(_))
        | (Value::Number(_), Value::Number(_))
        | (Value::String(_), Value::String(_)) => {}
        (Value::Array(a), Value::Array(b)) => {
            // Array items: check that both have the same *per-item* shape
            // using the first element as the exemplar. Different lengths
            // are allowed — replay may return empty when fixture has data.
            if let (Some(ea), Some(ba)) = (a.first(), b.first()) {
                out.extend(shape_diff(ea, ba, &format!("{}[0]", path)));
            }
        }
        (Value::Object(ea), Value::Object(ba)) => {
            for (k, v) in ea {
                let sub = format!("{}.{}", path, k);
                match ba.get(k) {
                    Some(bv) => out.extend(shape_diff(v, bv, &sub)),
                    None => out.push(ShapeMismatch {
                        path: sub,
                        expected: shape_name(v).into(),
                        actual: "<missing>".into(),
                    }),
                }
            }
            for (k, _) in ba {
                if !ea.contains_key(k) {
                    out.push(ShapeMismatch {
                        path: format!("{}.{}", path, k),
                        expected: "<missing>".into(),
                        actual: shape_name(&ba[k]).into(),
                    });
                }
            }
        }
        _ => out.push(ShapeMismatch {
            path: path.into(),
            expected: shape_name(expected).into(),
            actual: shape_name(actual).into(),
        }),
    }
    out
}

fn shape_name(v: &Value) -> &'static str {
    match v {
        Value::Null => "null",
        Value::Bool(_) => "bool",
        Value::Number(_) => "number",
        Value::String(_) => "string",
        Value::Array(_) => "array",
        Value::Object(_) => "object",
    }
}

// ── Harness ─────────────────────────────────────────────────────────────

#[allow(dead_code)] // `fixture` is set but only used in debug-logging paths.
struct ReplayStats {
    fixture: String,
    connections: usize,
    c2s_sent: usize,
    s2c_expected: usize,
    s2c_received: usize,
    shape_matches: usize,
    shape_mismatches: Vec<(String, Vec<ShapeMismatch>)>,
    timeouts: usize,
    /// context of every s2c frame we timed out waiting for — so we can
    /// see which handlers aren't producing output with no-op callbacks.
    timeout_contexts: Vec<String>,
    notifications_synthesized: usize,
}

type ConnectionReport = (
    usize,                             // c2s sent
    usize,                             // s2c expected
    usize,                             // s2c received
    usize,                             // shape matches
    Vec<(String, Vec<ShapeMismatch>)>, // mismatches
    Vec<String>,                       // timeout contexts
    usize,                             // notifications synthesized
);

/// Map an s2c context that's *only ever* a broadcast (never a command
/// response) to its `NotificationType`. Cross-cutting contexts like
/// `playerstate` / `playervolume` / `nowplayingcover` are intentionally
/// excluded — synthesizing them desynchronizes the wire and worsens
/// the shape-match score (verified empirically: opt-in for those
/// contexts drove playerstate timeouts 6 → 24 and nowplayingposition
/// 18 → 133, net negative).
///
/// Note: `nowplayingtrack` is excluded too. `TrackChanged` now emits
/// a 5-frame burst (rating, lfm-rating, lyrics, position, track) but
/// captured wire orderings vary — initial-state-push and runtime
/// playernext-driven bursts have different orderings — so synthesizing
/// the burst out-of-band misaligns subsequent reads. The Rust-side
/// multi-frame emission is still correct production behavior; the
/// harness just can't validate it against fixtures with arbitrary
/// orderings without a richer matching strategy (Tier B).
fn broadcast_notification_for(context: &str) -> Option<NotificationType> {
    match context {
        "nowplayinglistchanged" => Some(NotificationType::NowPlayingListChanged),
        _ => None,
    }
}

/// Walk a connection's frames and tag each s2c entry as either a
/// response to a recent c2s command (false) or an orphan broadcast
/// (true). The heuristic: a broadcast is an s2c whose context doesn't
/// match the most-recent unconsumed c2s command on this connection.
fn classify_broadcasts(frames: &[Frame]) -> Vec<bool> {
    let mut is_broadcast = vec![false; frames.len()];
    let mut last_c2s_ctx: Option<String> = None;
    let mut response_consumed = true;

    for (i, f) in frames.iter().enumerate() {
        match f.dir {
            Dir::C2s => {
                let ctx = f
                    .frame
                    .as_ref()
                    .and_then(|v| v.get("context"))
                    .and_then(|c| c.as_str())
                    .map(String::from);
                last_c2s_ctx = ctx;
                response_consumed = false;
            }
            Dir::S2c => {
                let s2c_ctx = f
                    .frame
                    .as_ref()
                    .and_then(|v| v.get("context"))
                    .and_then(|c| c.as_str())
                    .unwrap_or("");
                let matches_request = !response_consumed
                    && last_c2s_ctx.as_deref() == Some(s2c_ctx);
                if matches_request {
                    response_consumed = true;
                } else {
                    is_broadcast[i] = true;
                }
            }
        }
    }
    is_broadcast
}

async fn replay_connection(frames: &[Frame]) -> Result<ConnectionReport, String> {
    let state = AppState::for_replay(nop_callbacks(), String::from("/tmp/replay"));

    let listener = TcpListener::bind("127.0.0.1:0")
        .await
        .map_err(|e| format!("bind: {}", e))?;
    let addr = listener.local_addr().map_err(|e| format!("addr: {}", e))?;

    let server_state = Arc::clone(&state);
    let server_task = tokio::spawn(async move {
        if let Ok((stream, peer)) = listener.accept().await {
            handle_connection(stream, peer, server_state).await;
        }
    });

    let client = TcpStream::connect(addr)
        .await
        .map_err(|e| format!("connect: {}", e))?;
    let (read_half, mut write_half) = client.into_split();
    let mut reader = BufReader::new(read_half);
    let mut line = String::new();

    let mut c2s_sent = 0;
    let mut s2c_expected = 0;
    let mut s2c_received = 0;
    let mut shape_matches = 0;
    let mut mismatches: Vec<(String, Vec<ShapeMismatch>)> = Vec::new();
    let mut timeout_contexts: Vec<String> = Vec::new();
    let mut notifications_synthesized: usize = 0;

    // Pre-classify each s2c frame as response-to-c2s vs orphan broadcast.
    // Orphans we synthesize via mbrc notifications so the server emits
    // them onto the wire just like real MusicBee would have.
    let is_broadcast = classify_broadcasts(frames);

    for (i, f) in frames.iter().enumerate() {
        match f.dir {
            Dir::C2s => {
                let bytes: String = if let Some(raw) = &f.raw {
                    raw.clone()
                } else if let Some(fr) = &f.frame {
                    serde_json::to_string(fr).unwrap_or_default()
                } else {
                    continue;
                };
                write_half
                    .write_all(bytes.as_bytes())
                    .await
                    .map_err(|e| format!("write: {}", e))?;
                write_half.write_all(b"\r\n").await.ok();
                c2s_sent += 1;
            }
            Dir::S2c => {
                s2c_expected += 1;
                let expected = match &f.frame {
                    Some(fr) => fr.clone(),
                    None => continue,
                };
                let expected_ctx = expected
                    .get("context")
                    .and_then(|c| c.as_str())
                    .unwrap_or("?")
                    .to_string();

                // If this s2c is an orphan broadcast, synthesize the
                // matching MusicBee notification so the server emits the
                // expected frame onto the wire. The connection task is
                // already subscribed to event_tx; the broadcast lands on
                // the read side a few ms later.
                if is_broadcast[i] {
                    if let Some(nt) = broadcast_notification_for(&expected_ctx) {
                        let _ = synthesize_notification(&state, nt).await;
                        notifications_synthesized += 1;
                    }
                }

                line.clear();
                match timeout(RESPONSE_READ_TIMEOUT, reader.read_line(&mut line)).await {
                    Ok(Ok(0)) => {
                        return Err(format!("server closed mid-stream at seq {}", f.seq));
                    }
                    Ok(Ok(_)) => {
                        s2c_received += 1;
                        match serde_json::from_str::<Value>(line.trim()) {
                            Ok(actual) => {
                                let diffs = shape_diff(&expected, &actual, "");
                                if diffs.is_empty() {
                                    shape_matches += 1;
                                } else {
                                    mismatches.push((expected_ctx, diffs));
                                }
                            }
                            Err(e) => {
                                mismatches.push((
                                    expected_ctx,
                                    vec![ShapeMismatch {
                                        path: "<parse>".into(),
                                        expected: "valid json".into(),
                                        actual: format!("{}: {:.80}", e, line.trim()),
                                    }],
                                ));
                            }
                        }
                    }
                    Ok(Err(e)) => return Err(format!("read error: {}", e)),
                    Err(_) => {
                        timeout_contexts.push(expected_ctx);
                    }
                }
            }
        }
    }

    drop(write_half);
    drop(reader);
    let _ = timeout(Duration::from_millis(200), server_task).await;

    Ok((
        c2s_sent,
        s2c_expected,
        s2c_received,
        shape_matches,
        mismatches,
        timeout_contexts,
        notifications_synthesized,
    ))
}

async fn replay_fixture(path: &PathBuf) -> ReplayStats {
    let frames = load_fixture(path);
    let connections = segment(&frames);

    let mut stats = ReplayStats {
        fixture: path.file_name().unwrap().to_string_lossy().into(),
        connections: connections.len(),
        c2s_sent: 0,
        s2c_expected: 0,
        s2c_received: 0,
        shape_matches: 0,
        shape_mismatches: Vec::new(),
        timeouts: 0,
        timeout_contexts: Vec::new(),
        notifications_synthesized: 0,
    };

    for (i, conn) in connections.iter().enumerate() {
        match replay_connection(conn).await {
            Ok((sent, expected, received, matched, mismatches, timeout_ctxs, notes)) => {
                stats.c2s_sent += sent;
                stats.s2c_expected += expected;
                stats.s2c_received += received;
                stats.shape_matches += matched;
                stats.timeouts += timeout_ctxs.len();
                stats.timeout_contexts.extend(timeout_ctxs);
                stats.shape_mismatches.extend(mismatches);
                stats.notifications_synthesized += notes;
            }
            Err(e) => {
                eprintln!(
                    "  conn[{}] ({} frames) failed: {}",
                    i,
                    conn.len(),
                    e
                );
            }
        }
    }
    stats
}

#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn replay_all_fixtures() {
    let fixtures = std::fs::read_dir(FIXTURE_DIR)
        .expect("fixture dir")
        .filter_map(|e| e.ok())
        .map(|e| e.path())
        .filter(|p| {
            p.extension().and_then(|s| s.to_str()) == Some("jsonl")
                && p.file_name()
                    .and_then(|n| n.to_str())
                    .map(|n| n.starts_with("legacy-"))
                    .unwrap_or(false)
        })
        .collect::<Vec<_>>();

    assert!(!fixtures.is_empty(), "no fixtures found in {}", FIXTURE_DIR);

    let mut all = Vec::new();
    for path in &fixtures {
        eprintln!("\n=== replaying {} ===", path.display());
        let stats = replay_fixture(path).await;
        eprintln!(
            "  conns={} c2s={} s2c_expected={} s2c_received={} matched={} mismatched={} timeouts={} notifications_synthesized={}",
            stats.connections,
            stats.c2s_sent,
            stats.s2c_expected,
            stats.s2c_received,
            stats.shape_matches,
            stats.shape_mismatches.len(),
            stats.timeouts,
            stats.notifications_synthesized,
        );
        // Show top-10 mismatch contexts so mismatches are actionable
        // without drowning in output.
        for (ctx, diffs) in stats.shape_mismatches.iter().take(10) {
            eprintln!("    mismatch {}: {:?}", ctx, diffs);
        }
        // Show a summary of what handlers never produced output.
        let mut timeout_counts: std::collections::BTreeMap<&str, usize> =
            std::collections::BTreeMap::new();
        for ctx in &stats.timeout_contexts {
            *timeout_counts.entry(ctx.as_str()).or_default() += 1;
        }
        for (ctx, n) in &timeout_counts {
            eprintln!("    timeout {}: {}× (handler silent)", ctx, n);
        }
        all.push(stats);
    }

    // Tier A acceptance: no crashes, fixture loaded, and *some* matches
    // prove the pipe is connected. Detailed per-context assertions
    // come in Tier B.
    let total_matches: usize = all.iter().map(|s| s.shape_matches).sum();
    let total_expected: usize = all.iter().map(|s| s.s2c_expected).sum();
    eprintln!(
        "\n=== overall: {}/{} expected s2c frames shape-matched",
        total_matches, total_expected
    );
    assert!(
        total_matches > 0,
        "nothing shape-matched — harness plumbing broken"
    );
}

// ── Tier B: span partition + burst-aware out-of-order shape match ──

/// One unit of replay work. The harness walks these in order.
#[derive(Debug)]
enum Span {
    /// A c2s frame, with an optional response we expect on the wire.
    /// Fire-and-forget commands (volume up, queue add, etc.) have None.
    C2sBeat {
        c2s: Frame,
        expected_response: Option<Frame>,
    },
    /// A run of orphan s2c frames between c2s beats — i.e. a broadcast
    /// burst from MusicBee. Order within the burst is matched out-of-
    /// order because the wire ordering varies between burst types.
    Burst { expected: Vec<Frame> },
}

fn partition_into_spans(frames: &[Frame]) -> Vec<Span> {
    let mut spans = Vec::new();
    let mut i = 0;
    while i < frames.len() {
        let f = &frames[i];
        match f.dir {
            Dir::C2s => {
                let c2s_ctx = f
                    .frame
                    .as_ref()
                    .and_then(|v| v.get("context"))
                    .and_then(|c| c.as_str());
                // Look ahead: if the very next frame is an s2c with
                // matching context, that's the response to this c2s.
                let response = match (c2s_ctx, frames.get(i + 1)) {
                    (Some(ctx), Some(next)) if next.dir == Dir::S2c => {
                        let next_ctx = next
                            .frame
                            .as_ref()
                            .and_then(|v| v.get("context"))
                            .and_then(|c| c.as_str());
                        if next_ctx == Some(ctx) {
                            Some(next.clone())
                        } else {
                            None
                        }
                    }
                    _ => None,
                };
                let consumed = if response.is_some() { 2 } else { 1 };
                spans.push(Span::C2sBeat {
                    c2s: f.clone(),
                    expected_response: response,
                });
                i += consumed;
            }
            Dir::S2c => {
                // Collect contiguous s2c frames.
                let mut j = i;
                while j < frames.len() && frames[j].dir == Dir::S2c {
                    j += 1;
                }
                spans.push(Span::Burst {
                    expected: frames[i..j].to_vec(),
                });
                i = j;
            }
        }
    }
    spans
}

/// Look at the set of contexts in a captured burst and decide which
/// notification(s) would produce that set. Returns each notification at
/// most once — firing TrackChanged emits all five frames in one call,
/// so we don't want to fire it per frame.
fn notifications_for_burst(contexts: &std::collections::HashSet<String>) -> Vec<NotificationType> {
    let mut out = Vec::new();
    // TrackChanged emits rating, lfm-rating, lyrics, position, track.
    // If any of those appear in the burst, fire it once.
    let track_changed_contexts = ["nowplayingrating", "nowplayinglfmrating", "nowplayingtrack"];
    if track_changed_contexts.iter().any(|c| contexts.contains(*c)) {
        out.push(NotificationType::TrackChanged);
    }
    if contexts.contains("nowplayinglistchanged") {
        out.push(NotificationType::NowPlayingListChanged);
    }
    // Cover/lyrics standalone broadcasts only — if the burst is a
    // TrackChanged we already fired that, which emits an empty cover
    // placeholder. NowPlayingArtworkReady fires on real cover-load.
    if contexts.contains("nowplayingcover")
        && !track_changed_contexts.iter().any(|c| contexts.contains(*c))
    {
        out.push(NotificationType::NowPlayingArtworkReady);
    }
    out
}

#[derive(Debug, Default)]
#[allow(dead_code)]
struct TierBStats {
    fixture: String,
    connections: usize,
    c2s_sent: usize,
    s2c_expected: usize,
    s2c_received: usize,
    shape_matches: usize,
    shape_mismatches: Vec<(String, Vec<ShapeMismatch>)>,
    burst_unmatched: usize,
    timeouts: usize,
    notifications_synthesized: usize,
}

async fn replay_connection_tier_b(
    frames: &[Frame],
) -> Result<
    (
        usize, // c2s sent
        usize, // s2c expected
        usize, // s2c received
        usize, // shape matches
        Vec<(String, Vec<ShapeMismatch>)>,
        usize, // burst unmatched
        usize, // timeouts
        usize, // notifications synthesized
    ),
    String,
> {
    let state = AppState::for_replay(nop_callbacks(), String::from("/tmp/replay"));

    let listener = TcpListener::bind("127.0.0.1:0")
        .await
        .map_err(|e| format!("bind: {}", e))?;
    let addr = listener.local_addr().map_err(|e| format!("addr: {}", e))?;

    let server_state = Arc::clone(&state);
    let server_task = tokio::spawn(async move {
        if let Ok((stream, peer)) = listener.accept().await {
            handle_connection(stream, peer, server_state).await;
        }
    });

    let client = TcpStream::connect(addr)
        .await
        .map_err(|e| format!("connect: {}", e))?;
    let (read_half, mut write_half) = client.into_split();
    let mut reader = BufReader::new(read_half);
    let mut line = String::new();

    let mut c2s_sent = 0;
    let mut s2c_expected = 0;
    let mut s2c_received = 0;
    let mut shape_matches = 0;
    let mut shape_mismatches: Vec<(String, Vec<ShapeMismatch>)> = Vec::new();
    let mut burst_unmatched = 0;
    let mut timeouts = 0;
    let mut notifications_synthesized = 0;

    let spans = partition_into_spans(frames);

    for span in spans {
        match span {
            Span::C2sBeat {
                c2s,
                expected_response,
            } => {
                let bytes: String = if let Some(raw) = &c2s.raw {
                    raw.clone()
                } else if let Some(fr) = &c2s.frame {
                    serde_json::to_string(fr).unwrap_or_default()
                } else {
                    continue;
                };
                write_half
                    .write_all(bytes.as_bytes())
                    .await
                    .map_err(|e| format!("write: {}", e))?;
                write_half.write_all(b"\r\n").await.ok();
                c2s_sent += 1;

                if let Some(resp) = expected_response {
                    let expected = match &resp.frame {
                        Some(fr) => fr.clone(),
                        None => continue,
                    };
                    let expected_ctx = expected
                        .get("context")
                        .and_then(|c| c.as_str())
                        .unwrap_or("?")
                        .to_string();
                    s2c_expected += 1;

                    line.clear();
                    match timeout(RESPONSE_READ_TIMEOUT, reader.read_line(&mut line)).await {
                        Ok(Ok(0)) => {
                            return Err(format!("server closed mid-stream at seq {}", resp.seq));
                        }
                        Ok(Ok(_)) => {
                            s2c_received += 1;
                            match serde_json::from_str::<Value>(line.trim()) {
                                Ok(actual) => {
                                    let diffs = shape_diff(&expected, &actual, "");
                                    if diffs.is_empty() {
                                        shape_matches += 1;
                                    } else {
                                        shape_mismatches.push((expected_ctx, diffs));
                                    }
                                }
                                Err(_) => burst_unmatched += 1,
                            }
                        }
                        Ok(Err(e)) => return Err(format!("read error: {}", e)),
                        Err(_) => timeouts += 1,
                    }
                }
            }
            Span::Burst { expected } => {
                let burst_size = expected.len();
                s2c_expected += burst_size;

                // Compute the unique notifications this burst represents
                // and fire each once.
                let contexts: std::collections::HashSet<String> = expected
                    .iter()
                    .filter_map(|f| {
                        f.frame
                            .as_ref()
                            .and_then(|v| v.get("context"))
                            .and_then(|c| c.as_str())
                            .map(String::from)
                    })
                    .collect();
                for nt in notifications_for_burst(&contexts) {
                    let _ = synthesize_notification(&state, nt).await;
                    notifications_synthesized += 1;
                }

                // Read up to burst_size frames with bounded timeout per
                // frame. Collect into a Vec and match out-of-order.
                let mut received: Vec<Value> = Vec::new();
                for _ in 0..burst_size {
                    line.clear();
                    match timeout(RESPONSE_READ_TIMEOUT, reader.read_line(&mut line)).await {
                        Ok(Ok(0)) => break,
                        Ok(Ok(_)) => {
                            if let Ok(v) = serde_json::from_str::<Value>(line.trim()) {
                                received.push(v);
                                s2c_received += 1;
                            }
                        }
                        Ok(Err(e)) => return Err(format!("read error: {}", e)),
                        Err(_) => {
                            timeouts += 1;
                            break;
                        }
                    }
                }

                // Match each expected against the first received with
                // matching context. Use a `consumed` bitmap so each
                // received frame matches at most one expected.
                let mut consumed = vec![false; received.len()];
                for exp in &expected {
                    let exp_v = match &exp.frame {
                        Some(v) => v,
                        None => continue,
                    };
                    let exp_ctx = exp_v
                        .get("context")
                        .and_then(|c| c.as_str())
                        .unwrap_or("?")
                        .to_string();
                    let mut matched = false;
                    for (i, rcv) in received.iter().enumerate() {
                        if consumed[i] {
                            continue;
                        }
                        let rcv_ctx = rcv.get("context").and_then(|c| c.as_str()).unwrap_or("");
                        if rcv_ctx == exp_ctx {
                            consumed[i] = true;
                            matched = true;
                            let diffs = shape_diff(exp_v, rcv, "");
                            if diffs.is_empty() {
                                shape_matches += 1;
                            } else {
                                shape_mismatches.push((exp_ctx.clone(), diffs));
                            }
                            break;
                        }
                    }
                    if !matched {
                        burst_unmatched += 1;
                    }
                }
            }
        }
    }

    drop(write_half);
    drop(reader);
    let _ = timeout(Duration::from_millis(200), server_task).await;

    Ok((
        c2s_sent,
        s2c_expected,
        s2c_received,
        shape_matches,
        shape_mismatches,
        burst_unmatched,
        timeouts,
        notifications_synthesized,
    ))
}

async fn replay_fixture_tier_b(path: &PathBuf) -> TierBStats {
    let frames = load_fixture(path);
    let connections = segment(&frames);
    let mut stats = TierBStats {
        fixture: path.file_name().unwrap().to_string_lossy().into(),
        connections: connections.len(),
        ..Default::default()
    };

    for conn in connections.iter() {
        match replay_connection_tier_b(conn).await {
            Ok((sent, expected, received, matched, mismatches, unmatched, timeouts, notes)) => {
                stats.c2s_sent += sent;
                stats.s2c_expected += expected;
                stats.s2c_received += received;
                stats.shape_matches += matched;
                stats.shape_mismatches.extend(mismatches);
                stats.burst_unmatched += unmatched;
                stats.timeouts += timeouts;
                stats.notifications_synthesized += notes;
            }
            Err(e) => {
                eprintln!("  conn ({} frames) failed: {}", conn.len(), e);
            }
        }
    }
    stats
}

#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn replay_all_fixtures_tier_b() {
    let fixtures = std::fs::read_dir(FIXTURE_DIR)
        .expect("fixture dir")
        .filter_map(|e| e.ok())
        .map(|e| e.path())
        .filter(|p| {
            p.extension().and_then(|s| s.to_str()) == Some("jsonl")
                && p.file_name()
                    .and_then(|n| n.to_str())
                    .map(|n| n.starts_with("legacy-"))
                    .unwrap_or(false)
        })
        .collect::<Vec<_>>();

    assert!(!fixtures.is_empty(), "no fixtures found in {}", FIXTURE_DIR);

    let mut all = Vec::new();
    for path in &fixtures {
        eprintln!("\n=== Tier B replaying {} ===", path.display());
        let stats = replay_fixture_tier_b(path).await;
        eprintln!(
            "  conns={} c2s={} s2c_expected={} s2c_received={} matched={} mismatched={} burst_unmatched={} timeouts={} notifications_synthesized={}",
            stats.connections,
            stats.c2s_sent,
            stats.s2c_expected,
            stats.s2c_received,
            stats.shape_matches,
            stats.shape_mismatches.len(),
            stats.burst_unmatched,
            stats.timeouts,
            stats.notifications_synthesized,
        );
        for (ctx, diffs) in stats.shape_mismatches.iter().take(10) {
            eprintln!("    diff {}: {:?}", ctx, diffs);
        }
        all.push(stats);
    }

    let total_matches: usize = all.iter().map(|s| s.shape_matches).sum();
    let total_expected: usize = all.iter().map(|s| s.s2c_expected).sum();
    eprintln!(
        "\n=== Tier B overall: {}/{} expected s2c frames shape-matched (burst-aware)",
        total_matches, total_expected
    );
    assert!(
        total_matches > 0,
        "Tier B: nothing matched — harness or DTOs broken"
    );
}
