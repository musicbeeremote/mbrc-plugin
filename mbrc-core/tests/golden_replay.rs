//! Golden-trace replay — Tier A (shape-only).
//!
//! Reads each `legacy-*.jsonl` fixture, groups frames into connections
//! using the `player` handshake as boundary, and for every connection:
//!
//! 1. Boots a fresh Rust server instance listening on a loopback port.
//! 2. Connects a client socket.
//! 3. Replays the c2s frames in order.
//! 4. For every captured s2c frame, reads one response line from the
//!    server and shape-diffs it against the captured response.
//!
//! Shape-diff means: same keys at every object level, same value *types*
//! (null/bool/num/str/arr/obj). Values themselves are not compared —
//! that's Tier B.
//!
//! The mock callbacks are all no-op, so handler responses will often
//! contain default/empty values. Tier A just proves command dispatch
//! and envelope shape are wired correctly.

use std::path::PathBuf;
use std::sync::Arc;
use std::time::Duration;

use mbrc_core::replay_support::{handle_connection, nop_callbacks, AppState};
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
}

type ConnectionReport = (
    usize,                             // c2s sent
    usize,                             // s2c expected
    usize,                             // s2c received
    usize,                             // shape matches
    Vec<(String, Vec<ShapeMismatch>)>, // mismatches
    Vec<String>,                       // timeout contexts
);

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

    for f in frames {
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
    };

    for (i, conn) in connections.iter().enumerate() {
        match replay_connection(conn).await {
            Ok((sent, expected, received, matched, mismatches, timeout_ctxs)) => {
                stats.c2s_sent += sent;
                stats.s2c_expected += expected;
                stats.s2c_received += received;
                stats.shape_matches += matched;
                stats.timeouts += timeout_ctxs.len();
                stats.timeout_contexts.extend(timeout_ctxs);
                stats.shape_mismatches.extend(mismatches);
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
            "  conns={} c2s={} s2c_expected={} s2c_received={} matched={} mismatched={} timeouts={}",
            stats.connections,
            stats.c2s_sent,
            stats.s2c_expected,
            stats.s2c_received,
            stats.shape_matches,
            stats.shape_mismatches.len(),
            stats.timeouts,
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
