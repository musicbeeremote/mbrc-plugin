//! Trim raw tee-proxy captures in `tests/captures/` into committable
//! golden fixtures under `mbrc-core/tests/golden/`.
//!
//! Transformations, in order:
//! 1. Segment by connection boundary (`player` c2s frames).
//! 2. Classify each connection as (platform, protocol_version).
//! 3. Rewrite long cover base64 and lyric strings to tiny deterministic
//!    placeholders — envelope shape is preserved exactly.
//! 4. Dedup byte-identical frames (after substitution) within each
//!    (platform, protocol) bucket.
//! 5. Emit one JSONL per bucket to `mbrc-core/tests/golden/`.
//! 6. Extract the placeholder PNG once to
//!    `mbrc-core/tests/golden/_assets/placeholder-cover.png` so the
//!    replay harness can seed its mock callbacks with the same bytes.

use std::collections::HashSet;
use std::fs;
use std::io::{BufRead, BufReader, BufWriter, Write};
use std::path::{Path, PathBuf};

use base64::Engine;
use serde_json::{Map, Value};

const INPUT_DIR: &str = "tests/captures";
const OUTPUT_DIR: &str = "mbrc-core/tests/golden";

/// 82-byte 1×1 transparent PNG. Small enough to be a non-event in fixtures
/// but still a valid, decodable image for anyone debugging by hand.
const PLACEHOLDER_PNG_B64: &str =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNgAAIAAAUAAeIVWUMAAAAASUVORK5CYII=";

const PLACEHOLDER_LYRICS: &str = "<placeholder lyrics>";

/// Cover base64 payloads in real traces are 5-50KB. This threshold is
/// well above any valid status response ("200", "404", a short error
/// string) and well below any real cover.
const COVER_REWRITE_THRESHOLD: usize = 200;
const LYRIC_REWRITE_THRESHOLD: usize = 80;

/// Max items kept in any captured array. Real browse/list responses
/// have hundreds of entries; a fixture only needs a handful to pin the
/// per-item shape. The replay harness seeds its mock library with
/// exactly these items so byte-diffs still work.
const MAX_ARRAY_LEN: usize = 3;

fn main() -> std::io::Result<()> {
    fs::create_dir_all(OUTPUT_DIR)?;
    let assets_dir = Path::new(OUTPUT_DIR).join("_assets");
    fs::create_dir_all(&assets_dir)?;

    write_placeholder_asset(&assets_dir)?;

    let records = load_captures()?;
    println!("Loaded {} records from {}", records.len(), INPUT_DIR);

    let connections = segment_connections(&records);
    println!("Segmented into {} connections", connections.len());
    for (i, c) in connections.iter().enumerate() {
        println!(
            "  conn[{}] seq {}-{} ({} frames) platform={:?} protocol={:?}",
            i,
            records[c.start].seq(),
            records[c.end - 1].seq(),
            c.end - c.start,
            c.platform,
            c.protocol_version,
        );
    }

    let mut buckets: std::collections::BTreeMap<String, Vec<&Connection>> =
        std::collections::BTreeMap::new();
    for conn in &connections {
        buckets.entry(conn.bucket_name()).or_default().push(conn);
    }

    let mut total_kept = 0usize;
    for (bucket, conns) in &buckets {
        let path = Path::new(OUTPUT_DIR).join(format!("{}.jsonl", bucket));
        let mut writer = BufWriter::new(fs::File::create(&path)?);
        let mut seen_patterns: HashSet<String> = HashSet::new();
        let mut kept = 0usize;
        let mut conn_counter: u32 = 0;

        for conn in conns {
            // Connection-level dedup: if a previous connection in this
            // bucket had the same command sequence (same contexts/dirs/
            // shapes in the same order), skip this one entirely. Keeps
            // the fixture compact while preserving structurally distinct
            // connections — critical for per-connection replay.
            let pattern = connection_pattern(&records[conn.start..conn.end]);
            if !seen_patterns.insert(pattern) {
                continue;
            }

            let conn_id = conn_counter;
            conn_counter += 1;

            // Within-connection dedup: collapse immediate repeats
            // (identical pings, position broadcasts). Less aggressive
            // than the old global dedup so that each connection stays
            // self-contained and replayable.
            let mut seen_in_conn: HashSet<String> = HashSet::new();
            for rec in &records[conn.start..conn.end] {
                let mut rewritten = rewrite_placeholders(&rec.raw);
                if let Some(obj) = rewritten.as_object_mut() {
                    obj.insert("conn_id".into(), Value::from(conn_id));
                }
                let dedup_key = dedup_signature(&rewritten);
                if !seen_in_conn.insert(dedup_key) {
                    continue;
                }
                writeln!(writer, "{}", serde_json::to_string(&rewritten)?)?;
                kept += 1;
            }
        }
        println!(
            "  → {} ({} frames kept, {} unique connections)",
            path.display(),
            kept,
            conn_counter
        );
        total_kept += kept;
    }

    println!(
        "Done. Wrote {} fixtures, {} frames total, under {}",
        buckets.len(),
        total_kept,
        OUTPUT_DIR
    );
    Ok(())
}

// ── Record loading ──────────────────────────────────────────────────────

struct Record {
    raw: Value,
}

impl Record {
    fn seq(&self) -> u64 {
        self.raw.get("seq").and_then(|v| v.as_u64()).unwrap_or(0)
    }
    fn dir(&self) -> &str {
        self.raw.get("dir").and_then(|v| v.as_str()).unwrap_or("")
    }
    fn context(&self) -> Option<&str> {
        self.raw
            .get("frame")
            .and_then(|f| f.get("context"))
            .and_then(|c| c.as_str())
    }
    fn frame_data(&self) -> Option<&Value> {
        self.raw.get("frame").and_then(|f| f.get("data"))
    }
}

fn load_captures() -> std::io::Result<Vec<Record>> {
    let mut out = Vec::new();
    let mut entries: Vec<PathBuf> = fs::read_dir(INPUT_DIR)?
        .filter_map(|e| e.ok())
        .map(|e| e.path())
        .filter(|p| p.extension().and_then(|s| s.to_str()) == Some("jsonl"))
        .collect();
    entries.sort();

    for path in entries {
        let file = fs::File::open(&path)?;
        for line in BufReader::new(file).lines() {
            let line = line?;
            if line.trim().is_empty() {
                continue;
            }
            if let Ok(raw) = serde_json::from_str::<Value>(&line) {
                out.push(Record { raw });
            }
        }
    }
    Ok(out)
}

// ── Connection segmentation ─────────────────────────────────────────────

#[derive(Debug)]
struct Connection {
    start: usize,
    end: usize, // exclusive
    platform: Option<String>,
    protocol_version: Option<i64>,
}

impl Connection {
    fn bucket_name(&self) -> String {
        // v2/v3 only ever come from ApiDebugger synthetic sessions in
        // current practice (no shipping Android/iOS client declares
        // those). Drop the platform suffix so the filename doesn't
        // imply "this is what that platform does at v2".
        match self.protocol_version {
            Some(2) | Some(3) => format!(
                "legacy-v{}",
                self.protocol_version.unwrap()
            ),
            Some(v) => {
                let plat = match self.platform.as_deref() {
                    Some("Android") => "android",
                    Some("iOS") => "ios",
                    Some(_) => "other",
                    None => "apidebugger",
                };
                format!("legacy-v{}-{}", v, plat)
            }
            None => {
                let plat = match self.platform.as_deref() {
                    Some("Android") => "android",
                    Some("iOS") => "ios",
                    Some(_) => "other",
                    None => "apidebugger",
                };
                format!("legacy-unknown-{}", plat)
            }
        }
    }
}

fn segment_connections(records: &[Record]) -> Vec<Connection> {
    let mut out = Vec::new();
    let mut cur_start: Option<usize> = None;
    let mut cur_platform: Option<String> = None;
    let mut cur_version: Option<i64> = None;

    for (i, rec) in records.iter().enumerate() {
        // Only real handshakes send a *string* platform tag. ApiDebugger
        // emits `{"context":"player","data":{}}` mid-session from
        // SendInitialRequests — those must not split a connection.
        let handshake_player = rec.context() == Some("player")
            && rec.dir() == "c2s"
            && matches!(rec.frame_data(), Some(Value::String(_)));
        if handshake_player {
            if let Some(start) = cur_start {
                out.push(Connection {
                    start,
                    end: i,
                    platform: cur_platform.take(),
                    protocol_version: cur_version.take(),
                });
            }
            cur_start = Some(i);
            cur_platform = rec
                .frame_data()
                .and_then(|d| d.as_str())
                .map(|s| s.to_string());
            cur_version = None;
        }
        if rec.context() == Some("protocol") && rec.dir() == "c2s" {
            if let Some(data) = rec.frame_data() {
                cur_version = extract_protocol_version(data);
            }
        }
    }
    if let Some(start) = cur_start {
        out.push(Connection {
            start,
            end: records.len(),
            platform: cur_platform,
            protocol_version: cur_version,
        });
    }
    out
}

fn extract_protocol_version(data: &Value) -> Option<i64> {
    if let Some(n) = data.as_i64() {
        return Some(n); // V2 legacy bare int
    }
    data.get("protocol_version").and_then(|v| v.as_i64())
}

// ── Placeholder substitution ────────────────────────────────────────────

fn rewrite_placeholders(rec: &Value) -> Value {
    let mut out = rec.clone();
    let ctx = out
        .get("frame")
        .and_then(|f| f.get("context"))
        .and_then(|c| c.as_str())
        .map(|s| s.to_string());

    let Some(frame) = out.get_mut("frame") else {
        return out;
    };
    let Some(data) = frame.get_mut("data") else {
        return out;
    };

    match ctx.as_deref() {
        Some("libraryalbumcover") | Some("nowplayingcover") => {
            rewrite_cover(data);
        }
        Some("nowplayinglyrics") => {
            rewrite_lyrics(data);
        }
        _ => {}
    }
    truncate_arrays(data);
    out
}

/// Recursively clip every array in `v` to at most `MAX_ARRAY_LEN` items.
/// Preserves empty-array and short-array cases verbatim — only the bulk
/// list responses shrink.
fn truncate_arrays(v: &mut Value) {
    match v {
        Value::Array(a) => {
            if a.len() > MAX_ARRAY_LEN {
                a.truncate(MAX_ARRAY_LEN);
            }
            for item in a.iter_mut() {
                truncate_arrays(item);
            }
        }
        Value::Object(o) => {
            for (_, val) in o.iter_mut() {
                truncate_arrays(val);
            }
        }
        _ => {}
    }
}

fn rewrite_cover(v: &mut Value) {
    match v {
        Value::String(s) if s.len() > COVER_REWRITE_THRESHOLD => {
            *s = PLACEHOLDER_PNG_B64.to_string();
        }
        Value::Object(o) => rewrite_long_string_field(o, "cover", PLACEHOLDER_PNG_B64, COVER_REWRITE_THRESHOLD),
        _ => {}
    }
}

fn rewrite_lyrics(v: &mut Value) {
    match v {
        Value::String(s) if s.len() > LYRIC_REWRITE_THRESHOLD => {
            *s = PLACEHOLDER_LYRICS.to_string();
        }
        Value::Object(o) => rewrite_long_string_field(o, "lyrics", PLACEHOLDER_LYRICS, LYRIC_REWRITE_THRESHOLD),
        _ => {}
    }
}

fn rewrite_long_string_field(
    obj: &mut Map<String, Value>,
    field: &str,
    replacement: &str,
    threshold: usize,
) {
    if let Some(val) = obj.get_mut(field) {
        if let Value::String(s) = val {
            if s.len() > threshold {
                *s = replacement.to_string();
            }
        }
    }
}

// ── Dedup signature ─────────────────────────────────────────────────────

/// Stable string signature of a record that ignores volatile fields
/// (seq, ts, elapsed_ms) so identical frames from different captures
/// collapse into one fixture entry.
fn dedup_signature(rec: &Value) -> String {
    let mut trimmed = rec.clone();
    if let Some(obj) = trimmed.as_object_mut() {
        obj.remove("seq");
        obj.remove("ts");
        obj.remove("elapsed_ms");
    }
    serde_json::to_string(&trimmed).unwrap_or_default()
}

/// Structural signature for a whole connection — concatenated
/// (dir, context, shape-only-data) for each frame. Two connections with
/// the same command sequence produce the same pattern. Used to skip
/// re-emitting functionally identical reconnects.
fn connection_pattern(records: &[Record]) -> String {
    let mut parts = String::with_capacity(records.len() * 32);
    for rec in records {
        parts.push_str(rec.dir());
        parts.push(':');
        parts.push_str(rec.context().unwrap_or("RAW"));
        parts.push(':');
        if let Some(data) = rec.frame_data() {
            parts.push_str(&shape_only(data));
        }
        parts.push('\n');
    }
    parts
}

/// Canonical shape of a JSON value — types and object keys only, values
/// stripped. Two frames with identical structure but different values
/// canonicalize the same way.
fn shape_only(v: &Value) -> String {
    match v {
        Value::Null => "null".into(),
        Value::Bool(_) => "bool".into(),
        Value::Number(_) => "num".into(),
        Value::String(_) => "str".into(),
        Value::Array(a) => match a.first() {
            None => "arr[]".into(),
            Some(item) => format!("arr[{}]", shape_only(item)),
        },
        Value::Object(o) => {
            let mut keys: Vec<&String> = o.keys().collect();
            keys.sort();
            let inner: Vec<String> = keys
                .iter()
                .map(|k| format!("{}:{}", k, shape_only(&o[*k])))
                .collect();
            format!("{{{}}}", inner.join(","))
        }
    }
}

// ── Placeholder PNG asset ───────────────────────────────────────────────

fn write_placeholder_asset(dir: &Path) -> std::io::Result<()> {
    let path = dir.join("placeholder-cover.png");
    let bytes = base64::engine::general_purpose::STANDARD
        .decode(PLACEHOLDER_PNG_B64)
        .map_err(std::io::Error::other)?;
    fs::write(&path, bytes)?;
    println!("Wrote {} ({} bytes)", path.display(), PLACEHOLDER_PNG_B64.len() * 3 / 4);
    Ok(())
}
