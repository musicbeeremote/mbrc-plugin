//! The `mbrc-capture/2` golden-trace format.
//!
//! One JSON object per line. Two record kinds, discriminated by `type`:
//!
//! * **`frame`** ([`Frame`]) - a wire frame. Carries the *true* TCP `conn_id`
//!   (assigned at accept, not inferred from handshake frames), a global monotonic
//!   `seq`, the exact `raw` bytes (source of truth for byte-faithful replay) plus
//!   a derived `frame` parse when the bytes were valid JSON, and - for `s2c`
//!   frames - a `reply_to` hint (the seq of the most recent `c2s` on the
//!   connection) that separates responses from unsolicited broadcasts.
//! * **`meta`** - capture/connection lifecycle: `capture-start` (format header),
//!   `open`, `handshake` (client type + negotiated protocol), and `close`
//!   (reason + which side hung up first). Built by the `meta_*` constructors.
//!
//! Meta lines have no `seq`/`dir`, so a consumer that only wants frames can skip
//! them with a single field check - the format stays additive.
//!
//! This crate is the single definition of the format shared by the capture
//! producer (the api-debugger proxy), the CLI (`trim`/`inspect`), and the
//! replay harness, so a producer and a consumer can never drift.

use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use time::format_description::well_known::Rfc3339;
use time::OffsetDateTime;

pub mod schema;
pub mod trim;
pub use schema::{endpoint_schemas, endpoint_values, EndpointSchemas, EndpointValues, FieldMap};
pub use trim::{trim_capture, TrimOutput, PLACEHOLDER_LYRICS, PLACEHOLDER_PNG_B64};

/// Capture format version, written into the file header.
pub const CAPTURE_FORMAT: &str = "mbrc-capture/2";

/// RFC-3339 UTC timestamp, or the epoch if the clock can't be formatted.
pub fn now_rfc3339() -> String {
    OffsetDateTime::now_utc()
        .format(&Rfc3339)
        .unwrap_or_else(|_| "1970-01-01T00:00:00Z".to_owned())
}

fn frame_kind() -> String {
    "frame".to_string()
}

/// A captured wire frame - the golden-trace record written to disk.
///
/// Field order is significant: `type` serializes first to match the on-disk
/// shape consumers expect.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Frame {
    #[serde(rename = "type", default = "frame_kind")]
    kind: String,
    pub conn_id: u32,
    pub seq: u64,
    pub ts: String,
    /// "c2s" (client -> server) or "s2c" (server -> client).
    pub dir: String,
    pub elapsed_ms: u128,
    /// For `s2c` frames: seq of the most recent `c2s` on this connection, if any
    /// - a correlation hint distinguishing responses from broadcasts.
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub reply_to: Option<u64>,
    /// Exact frame content as it crossed the wire (line terminator stripped).
    /// The source of truth for byte-faithful replay.
    pub raw: String,
    /// Parsed JSON, present when `raw` was well-formed. Derived convenience.
    #[serde(skip_serializing_if = "Option::is_none", default)]
    pub frame: Option<Value>,
}

impl Frame {
    /// Build a frame record from the exact bytes seen on the wire, parsing a
    /// convenience `frame` copy when they are valid JSON.
    pub fn new(conn_id: u32, seq: u64, dir: &str, elapsed_ms: u128, frame_bytes: &[u8]) -> Self {
        let raw = String::from_utf8_lossy(frame_bytes).into_owned();
        // Lenient parse so iOS `\'`-quirk frames still get a usable `frame`
        // view; `raw` stays the exact wire bytes regardless.
        let frame = mbrc_wire::parse_lenient(&raw);
        Frame {
            kind: frame_kind(),
            conn_id,
            seq,
            ts: now_rfc3339(),
            dir: dir.to_string(),
            elapsed_ms,
            reply_to: None,
            raw,
            frame,
        }
    }

    /// `context` field of the parsed frame, if any.
    pub fn context(&self) -> Option<&str> {
        self.frame.as_ref()?.get("context")?.as_str()
    }
}

/// `capture-start` header: format version + the proxy's listen/upstream addrs.
pub fn meta_capture_start(listen: &str, upstream: &str) -> Value {
    json!({
        "type": "meta",
        "event": "capture-start",
        "format": CAPTURE_FORMAT,
        "listen": listen,
        "upstream": upstream,
        "ts": now_rfc3339(),
    })
}

/// `open`: a client connection was accepted.
pub fn meta_open(conn_id: u32, peer: &str) -> Value {
    json!({
        "type": "meta",
        "conn_id": conn_id,
        "event": "open",
        "peer": peer,
        "ts": now_rfc3339(),
    })
}

/// `handshake`: the negotiated client type + protocol version on a connection.
pub fn meta_handshake(
    conn_id: u32,
    client_type: Option<&str>,
    protocol_version: Option<i64>,
) -> Value {
    json!({
        "type": "meta",
        "conn_id": conn_id,
        "event": "handshake",
        "client_type": client_type,
        "protocol_version": protocol_version,
        "ts": now_rfc3339(),
    })
}

/// `close`: a connection ended, with the reason and which side hung up first.
pub fn meta_close(conn_id: u32, reason: &str, by: &str) -> Value {
    json!({
        "type": "meta",
        "conn_id": conn_id,
        "event": "close",
        "ts": now_rfc3339(),
        "reason": reason,
        "by": by,
    })
}

/// A parsed capture record: a typed [`Frame`] or a raw `meta` value.
#[derive(Debug, Clone)]
pub enum Record {
    Frame(Box<Frame>),
    Meta(Value),
}

/// Parse one capture line into a [`Record`]. Returns `None` for blank lines,
/// malformed JSON, or an unknown/absent `type`.
pub fn parse_line(line: &str) -> Option<Record> {
    let line = line.trim();
    if line.is_empty() {
        return None;
    }
    let v: Value = serde_json::from_str(line).ok()?;
    match v.get("type").and_then(|t| t.as_str()) {
        Some("frame") => serde_json::from_value(v)
            .ok()
            .map(|f| Record::Frame(Box::new(f))),
        Some("meta") => Some(Record::Meta(v)),
        _ => None,
    }
}

/// Count frame records cheaply, without a full JSON parse: non-empty lines
/// carrying a `"dir"` field (meta/lifecycle lines have none).
pub fn count_frames(contents: &str) -> usize {
    contents.lines().filter(|l| l.contains("\"dir\"")).count()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn frame_keeps_raw_and_parses_valid_json() {
        let r = Frame::new(0, 3, "c2s", 12, br#"{"context":"player","data":"Android"}"#);
        assert_eq!(r.raw, r#"{"context":"player","data":"Android"}"#);
        assert_eq!(r.context(), Some("player"));
        assert_eq!(r.conn_id, 0);
        assert_eq!(r.seq, 3);
    }

    #[test]
    fn frame_leaves_malformed_as_raw_only() {
        let r = Frame::new(1, 0, "s2c", 0, b"{not json");
        assert!(r.frame.is_none());
        assert_eq!(r.raw, "{not json");
        assert_eq!(r.context(), None);
    }

    #[test]
    fn frame_serializes_type_first_and_skips_none() {
        let r = Frame::new(0, 0, "c2s", 0, b"{}");
        let line = serde_json::to_string(&r).unwrap();
        assert!(
            line.starts_with(r#"{"type":"frame","conn_id":0"#),
            "got {line}"
        );
        // reply_to is None on a c2s frame, so it must not appear.
        assert!(!line.contains("reply_to"), "got {line}");
    }

    #[test]
    fn frame_round_trips() {
        let mut r = Frame::new(2, 5, "s2c", 7, br#"{"context":"nowplaying"}"#);
        r.reply_to = Some(4);
        let line = serde_json::to_string(&r).unwrap();
        match parse_line(&line) {
            Some(Record::Frame(f)) => {
                assert_eq!(f.conn_id, 2);
                assert_eq!(f.reply_to, Some(4));
                assert_eq!(f.context(), Some("nowplaying"));
            }
            other => panic!("expected frame, got {other:?}"),
        }
    }

    #[test]
    fn parse_line_classifies_meta_and_junk() {
        assert!(matches!(
            parse_line(r#"{"type":"meta","event":"open"}"#),
            Some(Record::Meta(_))
        ));
        assert!(parse_line("").is_none());
        assert!(parse_line("not json").is_none());
        assert!(parse_line(r#"{"no":"type"}"#).is_none());
    }

    #[test]
    fn count_frames_ignores_meta_lines() {
        let jsonl = concat!(
            "{\"type\":\"meta\",\"event\":\"capture-start\"}\n",
            "{\"type\":\"frame\",\"dir\":\"c2s\",\"seq\":0}\n",
            "{\"type\":\"frame\",\"dir\":\"s2c\",\"seq\":1}\n",
            "\n",
        );
        assert_eq!(count_frames(jsonl), 2);
    }

    #[test]
    fn meta_constructors_shape() {
        let h = meta_handshake(1, Some("Android"), Some(4));
        assert_eq!(h["type"], "meta");
        assert_eq!(h["event"], "handshake");
        assert_eq!(h["client_type"], "Android");
        assert_eq!(h["protocol_version"], 4);
        let s = meta_capture_start("0.0.0.0:3100", "127.0.0.1:3000");
        assert_eq!(s["format"], CAPTURE_FORMAT);
    }
}
