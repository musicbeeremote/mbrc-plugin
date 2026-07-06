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
//!    placeholders, cap arrays, scrub PII (file paths, library metadata), and
//!    cap frames per context - re-deriving `raw` when a frame changes.
//!
//! Scrubbing (step 4) makes the fixture safe to commit to a public repo: real
//! file paths (which leak usernames, host names, and library layout) are rewritten
//! to synthetic ones, and library metadata (artist/album/title/genre/name) is
//! replaced with deterministic pseudonyms. The mapping is stable within a bucket,
//! so a name or path referenced across frames (browse -> cover -> queue) stays
//! consistent and the fixture still replays.

use std::collections::{BTreeMap, HashSet};

use serde_json::{Map, Value};

use crate::{parse_line, Record};

/// 1x1 transparent PNG, base64. Small enough to be a non-event in fixtures but
/// still a valid decodable image for anyone debugging by hand.
pub const PLACEHOLDER_PNG_B64: &str =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNgAAIAAAUAAeIVWUMAAAAASUVORK5CYII=";

/// Placeholder for long lyric strings - lorem ipsum, so no real (copyrighted)
/// lyrics land in a committed fixture while the shape (a long multi-line string)
/// is preserved.
pub const PLACEHOLDER_LYRICS: &str = "Lorem ipsum dolor sit amet, consectetur \
adipiscing elit.\nSed do eiusmod tempor incididunt ut labore et dolore magna \
aliqua.\nUt enim ad minim veniam, quis nostrud exercitation ullamco laboris.";

/// Cover base64 payloads in real traces are 5-50 KB. This threshold sits well
/// above any status/error string ("200", "404") and below any real cover.
const COVER_REWRITE_THRESHOLD: usize = 200;
const LYRIC_REWRITE_THRESHOLD: usize = 80;

/// Max items kept in any captured array. Real browse/list responses have
/// hundreds; a fixture only needs a handful to pin the per-item shape.
const MAX_ARRAY_LEN: usize = 3;

/// Max frames kept per `(dir, context)` within a bucket. Real sessions repeat a
/// command hundreds of times (the browse screen fires one `libraryalbumcover`
/// per album); a fixture only needs a handful to pin the exchange. This is what
/// keeps a cover-fetch storm from dominating the fixture.
const MAX_PER_CONTEXT: usize = 8;

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

/// Deterministic pseudonymizer, stable within one bucket: the same original
/// value always maps to the same synthetic label, so cross-frame references
/// (an album named in `browsealbums`, requested in `libraryalbumcover`, echoed
/// in `nowplayinglist`) stay linked. Empty strings pass through unchanged so
/// the "empty album" shape is preserved.
#[derive(Default)]
struct Scrubber {
    counters: BTreeMap<&'static str, usize>,
    seen: BTreeMap<(&'static str, String), String>,
}

impl Scrubber {
    /// Map `original` to a stable `"{display} {n}"` label within `category`.
    fn label(&mut self, category: &'static str, display: &str, original: &str) -> String {
        if original.is_empty() {
            return String::new();
        }
        let key = (category, original.to_string());
        if let Some(v) = self.seen.get(&key) {
            return v.clone();
        }
        let n = self.counters.entry(category).or_insert(0);
        *n += 1;
        let val = format!("{display} {n}");
        self.seen.insert(key, val.clone());
        val
    }

    /// Map a path with no useful sibling metadata to a stable synthetic path.
    fn path_token(&mut self, original: &str, ext: &str) -> String {
        let key = ("path", original.to_string());
        if let Some(v) = self.seen.get(&key) {
            return v.clone();
        }
        let n = self.counters.entry("path").or_insert(0);
        *n += 1;
        let val = format!("\\\\host\\media\\item-{n}.{ext}");
        self.seen.insert(key, val.clone());
        val
    }
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
        // One scrubber per bucket: pseudonyms stay consistent across the whole
        // fixture file, but do not leak between platforms.
        let mut scrubber = Scrubber::default();

        for idxs in &conns {
            let pattern = connection_pattern(idxs, &frames);
            if !seen_patterns.insert(pattern) {
                continue; // structurally identical reconnect
            }
            let new_conn_id = conn_counter;
            conn_counter += 1;

            let mut seen_in_conn: HashSet<String> = HashSet::new();
            for &i in idxs {
                let mut rewritten = rewrite_placeholders(&frames[i].v, &mut scrubber);
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
        cap_per_context(&mut lines);
        out.buckets.insert(bucket, lines);
    }
    out
}

/// Keep at most `MAX_PER_CONTEXT` frames for each `(dir, context)`, in order.
/// This is what tames repetitive command storms (e.g. one `libraryalbumcover`
/// per album) so a single exchange cannot dominate the fixture.
fn cap_per_context(lines: &mut Vec<Value>) {
    let mut counts: BTreeMap<(String, String), usize> = BTreeMap::new();
    lines.retain(|rec| {
        let dir = rec
            .get("dir")
            .and_then(Value::as_str)
            .unwrap_or("")
            .to_string();
        let ctx = rec
            .get("frame")
            .and_then(|f| f.get("context"))
            .and_then(Value::as_str)
            .unwrap_or("")
            .to_string();
        let c = counts.entry((dir, ctx)).or_insert(0);
        *c += 1;
        *c <= MAX_PER_CONTEXT
    });
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

fn rewrite_placeholders(rec: &Value, scrub: &mut Scrubber) -> Value {
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
        scrub_value(data, scrub);
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

// -- PII scrubbing ---------------------------------------------------------

/// Recursively pseudonymize library metadata by key name and rewrite any file
/// paths, in place. Values are mapped through `scrub` so they stay consistent
/// across the whole fixture.
///
/// Two path strategies: `src`/`path`/`url` object fields are rebuilt from their
/// sibling metadata (readable, consistent); any *other* path-shaped string -
/// including bare paths inside arrays (`nowplayingqueue` sends folder paths as a
/// list) or under odd keys (`play`) - is caught by shape and tokenized. Nothing
/// path-shaped escapes.
fn scrub_value(v: &mut Value, scrub: &mut Scrubber) {
    match v {
        Value::Array(a) => {
            for item in a.iter_mut() {
                scrub_value(item, scrub);
            }
        }
        Value::Object(o) => {
            for (k, val) in o.iter_mut() {
                let category = match k.as_str() {
                    "artist" | "album_artist" | "albumartist" | "albumArtist" => {
                        Some(("artist", "Artist"))
                    }
                    "album" => Some(("album", "Album")),
                    "title" => Some(("title", "Track")),
                    "genre" => Some(("genre", "Genre")),
                    "name" => Some(("name", "Name")),
                    _ => None,
                };
                match category {
                    // Pseudonymize a metadata string field.
                    Some((cat, display)) if val.is_string() => {
                        let original = val.as_str().unwrap_or_default().to_string();
                        *val = Value::from(scrub.label(cat, display, &original));
                    }
                    // Leave src/path/url for rebuild_paths (it uses siblings);
                    // recurse everything else so nested/array paths get caught.
                    _ if matches!(k.as_str(), "src" | "path" | "url") => {}
                    _ => scrub_value(val, scrub),
                }
            }
            // Rebuild path fields last, so they can use the sibling pseudonyms
            // that were just assigned above.
            rebuild_paths(o, scrub);
        }
        // A path-shaped leaf string (bare array item, or an unrecognized key).
        Value::String(s) if looks_like_path(s) => {
            let ext = extension_of(s);
            *v = Value::from(scrub.path_token(s, &ext));
        }
        _ => {}
    }
}

/// Heuristic: does this string look like a filesystem path or URL? Real library
/// metadata (already pseudonymized before this runs) does not contain backslashes
/// or `scheme://`, and a `year` like "09/04/2006" has neither, so this only fires
/// on genuine paths.
fn looks_like_path(s: &str) -> bool {
    if s.contains('\\') || s.contains("://") {
        return true;
    }
    // Windows drive path with a forward slash, e.g. "C:/Music/...".
    let b = s.as_bytes();
    b.len() >= 3 && b[0].is_ascii_alphabetic() && b[1] == b':' && (b[2] == b'/' || b[2] == b'\\')
}

/// Rewrite `src`/`path`/`url` string fields on this object to synthetic paths.
/// Prefers rebuilding from the (already pseudonymized) sibling metadata so the
/// path stays human-readable and internally consistent; falls back to an opaque
/// token when there is nothing to build from.
fn rebuild_paths(o: &mut Map<String, Value>, scrub: &mut Scrubber) {
    let artist = o
        .get("album_artist")
        .and_then(Value::as_str)
        .or_else(|| o.get("artist").and_then(Value::as_str))
        .map(str::to_string);
    let album = o.get("album").and_then(Value::as_str).map(str::to_string);
    let title = o.get("title").and_then(Value::as_str).map(str::to_string);
    let name = o.get("name").and_then(Value::as_str).map(str::to_string);
    let trackno = o.get("trackno").and_then(Value::as_i64).unwrap_or(0);

    let mut updates: Vec<(&'static str, String)> = Vec::new();
    for key in ["src", "path", "url"] {
        if let Some(Value::String(p)) = o.get(key) {
            if p.is_empty() {
                continue;
            }
            let ext = extension_of(p);
            let synth = match (&artist, &album, &title) {
                (Some(a), Some(al), Some(t)) if !a.is_empty() && !t.is_empty() => {
                    format!("\\\\host\\music\\{a}\\{al}\\{trackno:02} - {t}.{ext}")
                }
                _ => match &name {
                    Some(n) if !n.is_empty() => format!("\\\\host\\media\\{n}.{ext}"),
                    _ => scrub.path_token(p, &ext),
                },
            };
            updates.push((key, synth));
        }
    }
    for (key, synth) in updates {
        o.insert(key.to_string(), Value::from(synth));
    }
}

/// Best-effort file extension (defaults to `dat`), used only to keep the
/// synthetic path's shape close to the original.
fn extension_of(p: &str) -> String {
    match p.rsplit('.').next() {
        Some(e) if !e.is_empty() && e.len() <= 4 && !e.contains(['\\', '/']) => e.to_string(),
        _ => "dat".to_string(),
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
    fn scrubs_paths_and_pseudonymizes_names() {
        // Synthetic inputs only - never paste real captured library data into a
        // committed test, or the test file leaks the PII the scrubber removes.
        let lines = [
            handshake_line(0, "Android", 4),
            frame_line(0, 0, "c2s", "player", Value::from("Android")),
            frame_line(
                0,
                1,
                "s2c",
                "nowplayingtrack",
                serde_json::json!({
                    "artist": "SENTINEL_ARTIST",
                    "title": "SENTINEL_TITLE",
                    "album": "SENTINEL_ALBUM",
                    "trackno": 10,
                    "path": "\\\\SENTINEL_HOST\\share\\SENTINEL_ARTIST\\SENTINEL_ALBUM\\10 - SENTINEL_TITLE.mp3"
                }),
            ),
        ]
        .join("\n");
        let out = trim_capture(&lines);
        let bucket = &out.buckets["legacy-v4-android"];
        let track = bucket
            .iter()
            .find(|r| r["frame"]["context"] == "nowplayingtrack")
            .unwrap();
        let data = &track["frame"]["data"];

        // Names pseudonymized.
        assert_eq!(data["artist"], Value::from("Artist 1"));
        assert_eq!(data["album"], Value::from("Album 1"));
        assert_eq!(data["title"], Value::from("Track 1"));

        // Path rebuilt from pseudonyms - no original host/layout, keeps extension.
        let path = data["path"].as_str().unwrap();
        assert!(path.contains("Artist 1") && path.contains("Track 1"));
        assert!(path.ends_with(".mp3"));

        // No original sentinel value survives anywhere in the re-derived frame.
        let raw = track["raw"].as_str().unwrap();
        for sentinel in [
            "SENTINEL_HOST",
            "SENTINEL_ARTIST",
            "SENTINEL_ALBUM",
            "SENTINEL_TITLE",
        ] {
            assert!(!raw.contains(sentinel), "{sentinel} leaked: {raw}");
        }
    }

    #[test]
    fn pseudonyms_are_stable_across_frames() {
        let lines = [
            handshake_line(0, "Android", 4),
            frame_line(0, 0, "c2s", "player", Value::from("Android")),
            frame_line(
                0,
                1,
                "s2c",
                "a",
                serde_json::json!({"artist": "SENTINEL_ARTIST"}),
            ),
            frame_line(
                0,
                2,
                "s2c",
                "b",
                serde_json::json!({"artist": "SENTINEL_ARTIST"}),
            ),
        ]
        .join("\n");
        let out = trim_capture(&lines);
        let bucket = &out.buckets["legacy-v4-android"];
        let a = bucket
            .iter()
            .find(|r| r["frame"]["context"] == "a")
            .unwrap();
        let b = bucket
            .iter()
            .find(|r| r["frame"]["context"] == "b")
            .unwrap();
        assert_eq!(a["frame"]["data"]["artist"], b["frame"]["data"]["artist"]);
        assert_eq!(a["frame"]["data"]["artist"], Value::from("Artist 1"));
    }

    #[test]
    fn scrubs_bare_paths_in_arrays_and_odd_keys() {
        // `nowplayingqueue` sends folder paths as a bare-string array plus a
        // `play` field - neither is a src/path/url key.
        let lines = [
            handshake_line(0, "Android", 4),
            frame_line(0, 0, "c2s", "player", Value::from("Android")),
            frame_line(
                0,
                1,
                "c2s",
                "nowplayingqueue",
                serde_json::json!({
                    "queue": "now",
                    "data": [
                        "\\\\SENTINEL_HOST\\share\\folder\\x",
                        "\\\\SENTINEL_HOST\\share\\folder\\x"
                    ],
                    "play": "\\\\SENTINEL_HOST\\share\\other\\y.mp3"
                }),
            ),
        ]
        .join("\n");
        let out = trim_capture(&lines);
        let q = out.buckets["legacy-v4-android"]
            .iter()
            .find(|r| r["frame"]["context"] == "nowplayingqueue")
            .unwrap();
        let raw = q["raw"].as_str().unwrap();
        assert!(!raw.contains("SENTINEL_HOST"), "path leaked in raw: {raw}");
        // Same original path -> same token (referential consistency).
        let arr = q["frame"]["data"]["data"].as_array().unwrap();
        assert_eq!(arr[0], arr[1]);
        assert!(arr[0]
            .as_str()
            .unwrap()
            .starts_with("\\\\host\\media\\item-"));
    }

    #[test]
    fn caps_frames_per_context() {
        let mut lines = vec![
            handshake_line(0, "Android", 4),
            frame_line(0, 0, "c2s", "player", Value::from("Android")),
        ];
        // 20 distinct cover requests - a cover-fetch storm.
        for i in 0..20u64 {
            lines.push(frame_line(
                0,
                100 + i,
                "c2s",
                "libraryalbumcover",
                Value::from(format!("album-{i}")),
            ));
        }
        let out = trim_capture(&lines.join("\n"));
        let bucket = &out.buckets["legacy-v4-android"];
        let covers = bucket
            .iter()
            .filter(|r| r["frame"]["context"] == "libraryalbumcover")
            .count();
        assert_eq!(covers, MAX_PER_CONTEXT);
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
