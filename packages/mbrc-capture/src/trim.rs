//! Trim raw `mbrc-capture/2` traces into committable golden fixtures.
//!
//! Unlike the old pre-`mbrc-capture/2` pipeline, connection boundaries and
//! platform/protocol are *not* inferred from `player` frames: the capture
//! already carries a true TCP `conn_id` on every frame and a `handshake` meta
//! record per connection, so we trust those directly.
//!
//! Transformations, in order:
//! 1. Group frames by `conn_id`; classify each connection via its `handshake`
//!    meta record (falling back to sniffing `player`/`protocol` frames).
//! 2. Bucket connections by `(platform, protocol_version)`.
//! 3. Drop pre-V4 buckets (the server rejects them at handshake).
//! 4. Per bucket: skip structurally-identical reconnects, renumber `conn_id`,
//!    collapse immediate repeats, rewrite long cover/lyric payloads to tiny
//!    placeholders, and cap arrays - re-deriving `raw` when a frame changes.

use std::collections::{BTreeMap, HashSet};

use serde_json::{Map, Value};

use crate::{parse_line, Record};

/// 1x1 transparent PNG, base64. Small enough to be a non-event in fixtures but
/// still a valid decodable image for anyone debugging by hand.
pub const PLACEHOLDER_PNG_B64: &str =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNgAAIAAAUAAeIVWUMAAAAASUVORK5CYII=";

/// Placeholder for long lyric strings.
pub const PLACEHOLDER_LYRICS: &str = "<placeholder lyrics>";

/// Cover base64 payloads in real traces are 5-50 KB. This threshold sits well
/// above any status/error string ("200", "404") and below any real cover.
const COVER_REWRITE_THRESHOLD: usize = 200;
const LYRIC_REWRITE_THRESHOLD: usize = 80;

/// Max items kept in any captured array. Real browse/list responses have
/// hundreds; a fixture only needs a handful to pin the per-item shape.
const MAX_ARRAY_LEN: usize = 3;

/// The result of trimming: one bucket (e.g. `legacy-v4-android`) per
/// `(platform, protocol)`, each a list of trimmed frame records.
#[derive(Debug, Default)]
pub struct TrimOutput {
    pub buckets: BTreeMap<String, Vec<Value>>,
}

impl TrimOutput {
    /// Total frames kept across all buckets.
    pub fn total_frames(&self) -> usize {
        self.buckets.values().map(Vec::len).sum()
    }
}

/// One capture frame record, kept as its raw JSON value for faithful rewrite.
struct FrameRec {
    v: Value,
    conn_id: u32,
}

impl FrameRec {
    fn dir(&self) -> &str {
        self.v.get("dir").and_then(Value::as_str).unwrap_or("")
    }
    fn context(&self) -> Option<&str> {
        self.v
            .get("frame")
            .and_then(|f| f.get("context"))
            .and_then(Value::as_str)
    }
    fn frame_data(&self) -> Option<&Value> {
        self.v.get("frame").and_then(|f| f.get("data"))
    }
}

#[derive(Default, Clone)]
struct Handshake {
    client_type: Option<String>,
    protocol_version: Option<i64>,
}

/// Trim the concatenated contents of one or more `mbrc-capture/2` files.
pub fn trim_capture(contents: &str) -> TrimOutput {
    // Parse frames (in order) and collect per-conn handshake metadata.
    let mut frames: Vec<FrameRec> = Vec::new();
    let mut handshakes: BTreeMap<u32, Handshake> = BTreeMap::new();

    for line in contents.lines() {
        match parse_line(line) {
            Some(Record::Frame(f)) => {
                frames.push(FrameRec {
                    conn_id: f.conn_id,
                    v: serde_json::to_value(&*f).unwrap_or(Value::Null),
                });
            }
            Some(Record::Meta(m))
                if m.get("event").and_then(Value::as_str) == Some("handshake") =>
            {
                if let Some(cid) = m.get("conn_id").and_then(Value::as_u64) {
                    let hs = handshakes.entry(cid as u32).or_default();
                    hs.client_type = m
                        .get("client_type")
                        .and_then(Value::as_str)
                        .map(str::to_owned);
                    hs.protocol_version = m.get("protocol_version").and_then(Value::as_i64);
                }
            }
            _ => {}
        }
    }

    // Group frame indices by conn_id, in first-seen order.
    let mut order: Vec<u32> = Vec::new();
    let mut by_conn: BTreeMap<u32, Vec<usize>> = BTreeMap::new();
    for (i, fr) in frames.iter().enumerate() {
        by_conn.entry(fr.conn_id).or_insert_with(|| {
            order.push(fr.conn_id);
            Vec::new()
        });
        by_conn.get_mut(&fr.conn_id).unwrap().push(i);
    }

    // Bucket connections by (platform, protocol), preserving first-seen order.
    let mut buckets: BTreeMap<String, Vec<Vec<usize>>> = BTreeMap::new();
    for cid in &order {
        let idxs = &by_conn[cid];
        let hs = classify(*cid, idxs, &frames, &handshakes);
        let bucket = bucket_name(hs.client_type.as_deref(), hs.protocol_version);
        // Pre-V4 connections are rejected at handshake and have no fixture value.
        if bucket.starts_with("legacy-v2") || bucket.starts_with("legacy-v3") {
            continue;
        }
        buckets.entry(bucket).or_default().push(idxs.clone());
    }

    let mut out = TrimOutput::default();
    for (bucket, conns) in buckets {
        let mut seen_patterns: HashSet<String> = HashSet::new();
        let mut lines: Vec<Value> = Vec::new();
        let mut conn_counter: u32 = 0;

        for idxs in &conns {
            let pattern = connection_pattern(idxs, &frames);
            if !seen_patterns.insert(pattern) {
                continue; // structurally identical reconnect
            }
            let new_conn_id = conn_counter;
            conn_counter += 1;

            let mut seen_in_conn: HashSet<String> = HashSet::new();
            for &i in idxs {
                let mut rewritten = rewrite_placeholders(&frames[i].v);
                if let Some(obj) = rewritten.as_object_mut() {
                    obj.insert("conn_id".into(), Value::from(new_conn_id));
                }
                let key = dedup_signature(&rewritten);
                if !seen_in_conn.insert(key) {
                    continue; // immediate repeat (ping, position broadcast)
                }
                lines.push(rewritten);
            }
        }
        out.buckets.insert(bucket, lines);
    }
    out
}

/// Classify a connection: prefer its `handshake` meta, else sniff the
/// connection's own `player` (c2s string) and `protocol` (c2s) frames.
fn classify(
    cid: u32,
    idxs: &[usize],
    frames: &[FrameRec],
    handshakes: &BTreeMap<u32, Handshake>,
) -> Handshake {
    let mut hs = handshakes.get(&cid).cloned().unwrap_or_default();
    if hs.client_type.is_none() {
        for &i in idxs {
            let fr = &frames[i];
            if fr.context() == Some("player") && fr.dir() == "c2s" {
                if let Some(Value::String(s)) = fr.frame_data() {
                    hs.client_type = Some(s.clone());
                    break;
                }
            }
        }
    }
    if hs.protocol_version.is_none() {
        for &i in idxs {
            let fr = &frames[i];
            if fr.context() == Some("protocol") && fr.dir() == "c2s" {
                if let Some(data) = fr.frame_data() {
                    hs.protocol_version = extract_protocol_version(data);
                    break;
                }
            }
        }
    }
    hs
}

fn extract_protocol_version(data: &Value) -> Option<i64> {
    if let Some(n) = data.as_i64() {
        return Some(n); // V2 legacy bare int
    }
    data.get("protocol_version").and_then(Value::as_i64)
}

fn bucket_name(platform: Option<&str>, protocol: Option<i64>) -> String {
    let plat = match platform {
        Some("Android") => "android",
        Some("iOS") => "ios",
        Some(_) => "other",
        None => "apidebugger",
    };
    match protocol {
        Some(2) | Some(3) => format!("legacy-v{}", protocol.unwrap()),
        Some(v) => format!("legacy-v{v}-{plat}"),
        None => format!("legacy-unknown-{plat}"),
    }
}

// -- Placeholder substitution --------------------------------------------

fn rewrite_placeholders(rec: &Value) -> Value {
    let mut out = rec.clone();
    let ctx = out
        .get("frame")
        .and_then(|f| f.get("context"))
        .and_then(Value::as_str)
        .map(str::to_owned);

    let changed = {
        let Some(frame) = out.get_mut("frame") else {
            return out;
        };
        let Some(data) = frame.get_mut("data") else {
            return out;
        };
        let before = data.clone();
        match ctx.as_deref() {
            Some("libraryalbumcover") | Some("nowplayingcover") => rewrite_cover(data),
            Some("nowplayinglyrics") => rewrite_lyrics(data),
            _ => {}
        }
        truncate_arrays(data);
        *data != before
    };

    // A rewritten frame's `raw` (exact wire bytes) no longer matches the parsed
    // frame, so re-derive it. preserve_order keeps the key order faithful.
    if changed {
        if let Some(frame) = out.get("frame") {
            let raw = serde_json::to_string(frame).unwrap_or_default();
            if let Some(obj) = out.as_object_mut() {
                obj.insert("raw".into(), Value::from(raw));
            }
        }
    }
    out
}

/// Recursively clip every array to at most `MAX_ARRAY_LEN` items.
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
        Value::Object(o) => {
            rewrite_long_string_field(o, "cover", PLACEHOLDER_PNG_B64, COVER_REWRITE_THRESHOLD)
        }
        _ => {}
    }
}

fn rewrite_lyrics(v: &mut Value) {
    match v {
        Value::String(s) if s.len() > LYRIC_REWRITE_THRESHOLD => {
            *s = PLACEHOLDER_LYRICS.to_string();
        }
        Value::Object(o) => {
            rewrite_long_string_field(o, "lyrics", PLACEHOLDER_LYRICS, LYRIC_REWRITE_THRESHOLD)
        }
        _ => {}
    }
}

fn rewrite_long_string_field(
    obj: &mut Map<String, Value>,
    field: &str,
    replacement: &str,
    threshold: usize,
) {
    if let Some(Value::String(s)) = obj.get_mut(field) {
        if s.len() > threshold {
            *s = replacement.to_string();
        }
    }
}

// -- Dedup -----------------------------------------------------------------

/// Signature ignoring volatile fields (seq, ts, elapsed_ms) so identical
/// frames collapse.
fn dedup_signature(rec: &Value) -> String {
    let mut trimmed = rec.clone();
    if let Some(obj) = trimmed.as_object_mut() {
        obj.remove("seq");
        obj.remove("ts");
        obj.remove("elapsed_ms");
    }
    serde_json::to_string(&trimmed).unwrap_or_default()
}

/// Structural signature of a whole connection - (dir, context, shape) per
/// frame. Identical command sequences from reconnects produce the same pattern.
fn connection_pattern(idxs: &[usize], frames: &[FrameRec]) -> String {
    let mut parts = String::with_capacity(idxs.len() * 32);
    for &i in idxs {
        let fr = &frames[i];
        parts.push_str(fr.dir());
        parts.push(':');
        parts.push_str(fr.context().unwrap_or("RAW"));
        parts.push(':');
        if let Some(data) = fr.frame_data() {
            parts.push_str(&shape_only(data));
        }
        parts.push('\n');
    }
    parts
}

/// Canonical shape of a JSON value - types and object keys only.
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
            let inner: Vec<String> = o
                .iter()
                .map(|(k, val)| format!("{k}:{}", shape_only(val)))
                .collect();
            format!("{{{}}}", inner.join(","))
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::Frame;

    /// Build one capture line for a frame with the given conn/dir/context/data.
    fn frame_line(conn_id: u32, seq: u64, dir: &str, context: &str, data: Value) -> String {
        let raw =
            serde_json::to_string(&serde_json::json!({"context": context, "data": data})).unwrap();
        let mut f = Frame::new(conn_id, seq, dir, 0, raw.as_bytes());
        f.conn_id = conn_id;
        serde_json::to_string(&f).unwrap()
    }

    fn handshake_line(conn_id: u32, client_type: &str, version: i64) -> String {
        serde_json::to_string(&crate::meta_handshake(
            conn_id,
            Some(client_type),
            Some(version),
        ))
        .unwrap()
    }

    #[test]
    fn buckets_by_handshake_meta() {
        let lines = [
            handshake_line(0, "Android", 4),
            frame_line(0, 0, "c2s", "player", Value::from("Android")),
            frame_line(0, 1, "s2c", "player", Value::from("MusicBee")),
            handshake_line(1, "iOS", 4),
            frame_line(1, 2, "c2s", "player", Value::from("iOS")),
        ]
        .join("\n");
        let out = trim_capture(&lines);
        assert!(out.buckets.contains_key("legacy-v4-android"));
        assert!(out.buckets.contains_key("legacy-v4-ios"));
    }

    #[test]
    fn drops_pre_v4() {
        let lines = [
            handshake_line(0, "Android", 2),
            frame_line(0, 0, "c2s", "player", Value::from("Android")),
        ]
        .join("\n");
        let out = trim_capture(&lines);
        assert!(
            out.buckets.is_empty(),
            "v2 should be dropped: {:?}",
            out.buckets.keys().collect::<Vec<_>>()
        );
    }

    #[test]
    fn caps_arrays_and_rewrites_cover() {
        let big_cover = "A".repeat(500);
        let lines = [
            handshake_line(0, "Android", 4),
            frame_line(0, 0, "c2s", "player", Value::from("Android")),
            frame_line(
                0,
                1,
                "s2c",
                "nowplayingcover",
                Value::from(big_cover.clone()),
            ),
            frame_line(
                0,
                2,
                "s2c",
                "library",
                Value::from((0..10).map(Value::from).collect::<Vec<_>>()),
            ),
        ]
        .join("\n");
        let out = trim_capture(&lines);
        let bucket = &out.buckets["legacy-v4-android"];
        // Cover payload replaced by the placeholder, and raw re-derived to match.
        let cover = bucket
            .iter()
            .find(|r| r["frame"]["context"] == "nowplayingcover")
            .unwrap();
        assert_eq!(cover["frame"]["data"], Value::from(PLACEHOLDER_PNG_B64));
        assert!(cover["raw"].as_str().unwrap().contains(PLACEHOLDER_PNG_B64));
        assert!(!cover["raw"].as_str().unwrap().contains(&big_cover));
        // Array capped to 3.
        let lib = bucket
            .iter()
            .find(|r| r["frame"]["context"] == "library")
            .unwrap();
        assert_eq!(
            lib["frame"]["data"].as_array().unwrap().len(),
            MAX_ARRAY_LEN
        );
    }

    #[test]
    fn renumbers_conn_id_per_bucket() {
        let lines = [
            handshake_line(5, "Android", 4),
            frame_line(5, 0, "c2s", "player", Value::from("Android")),
            frame_line(5, 1, "c2s", "playpause", Value::from("")),
        ]
        .join("\n");
        let out = trim_capture(&lines);
        let bucket = &out.buckets["legacy-v4-android"];
        assert!(bucket.iter().all(|r| r["conn_id"] == 0));
    }
}
