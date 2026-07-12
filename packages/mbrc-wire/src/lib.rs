//! MusicBee Remote legacy wire protocol primitives.
//!
//! The wire is `\r\n`-delimited JSON of the shape `{"context": "...", "data": ...}`.
//! This crate holds the pieces shared by every peer that speaks it - the
//! api-debugger's client and proxy, the headless CLI, and (later) the Rust core's
//! server side - so the framing, message shape, and handshake automation have a
//! single definition instead of a copy per tool.

use serde::{Deserialize, Serialize};

/// Line terminator for the legacy CRLF-JSON protocol.
pub const TERMINATOR: &str = "\r\n";

/// Handshake / keepalive context names.
pub const CTX_PLAYER: &str = "player";
pub const CTX_PROTOCOL: &str = "protocol";
pub const CTX_PING: &str = "ping";
pub const CTX_PONG: &str = "pong";

/// A wire message: `{"context": "...", "data": ...}`.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Message {
    pub context: String,
    #[serde(default)]
    pub data: serde_json::Value,
}

/// Extract the `context` field from a wire line without a full typed parse.
/// Returns `None` for malformed JSON or a missing/non-string `context`.
/// Tolerant of the iOS `\'` quirk (see [`parse_lenient`]).
pub fn parse_context(line: &str) -> Option<String> {
    parse_lenient(line)?
        .get("context")?
        .as_str()
        .map(str::to_owned)
}

/// Parse a wire line, tolerating the known iOS-client malformations.
///
/// Tries strict JSON first; on failure, retries after rewriting the iOS `\'`
/// escape (see [`sanitize_ios_quotes`]) and then after quoting a bare-identifier
/// `data` value (see [`sanitize_ios_bare_data`]) - the iOS v4 bug where it emits
/// e.g. `{"context":"nowplayingposition","data":status}` instead of
/// `"data":"status"`. Both are known v4 iOS quirks and must be parsed, not
/// dropped, or the frame (a control command or a position poll) silently
/// vanishes. Genuinely unrecoverable frames still return `None`.
pub fn parse_lenient(line: &str) -> Option<serde_json::Value> {
    if let Ok(v) = serde_json::from_str(line) {
        return Some(v);
    }
    let quoted = sanitize_ios_quotes(line);
    if let Ok(v) = serde_json::from_str(&quoted) {
        return Some(v);
    }
    serde_json::from_str(&sanitize_ios_bare_data(&quoted)?).ok()
}

/// Quote a bare-identifier `data` value - the iOS v4 bug where it emits
/// `{"data":status}` instead of `{"data":"status"}`. Rewrites the value only when
/// it is an unquoted identifier that is NOT a JSON literal (`true`/`false`/`null`),
/// number, string, object, or array, so already-valid frames are never touched.
/// Returns `None` when there is no such bare value to fix.
fn sanitize_ios_bare_data(s: &str) -> Option<String> {
    const KEY: &str = "\"data\":";
    let key_at = s.find(KEY)?;
    let val_at = key_at + KEY.len();
    let after = &s[val_at..];
    let lead_ws = after.len() - after.trim_start().len();
    let token_at = val_at + lead_ws;
    let first = s[token_at..].chars().next()?;
    // A valid JSON value start (string/object/array/number) is not the bug.
    if matches!(first, '"' | '{' | '[' | '-') || first.is_ascii_digit() {
        return None;
    }
    // The bare token is the run of identifier chars.
    let token: String = s[token_at..]
        .chars()
        .take_while(|c| c.is_ascii_alphanumeric() || *c == '_')
        .collect();
    // Empty, or a real JSON literal -> nothing to fix.
    if token.is_empty() || matches!(token.as_str(), "true" | "false" | "null") {
        return None;
    }
    let token_end = token_at + token.len();
    Some(format!(
        "{}\"{}\"{}",
        &s[..token_at],
        token,
        &s[token_end..]
    ))
}

/// Rewrite the iOS `\'` escape (invalid JSON) to a bare `'`. Escaped
/// backslashes (`\\`) are consumed atomically so a real `\\` before a quote is
/// never mangled - lossless for already-valid JSON.
pub fn sanitize_ios_quotes(s: &str) -> String {
    let mut out = String::with_capacity(s.len());
    let mut chars = s.chars().peekable();
    while let Some(c) = chars.next() {
        if c == '\\' {
            match chars.peek() {
                Some('\\') => {
                    out.push('\\');
                    out.push('\\');
                    chars.next();
                }
                Some('\'') => {
                    out.push('\'');
                    chars.next();
                }
                _ => out.push('\\'),
            }
        } else {
            out.push(c);
        }
    }
    out
}

/// Append the line terminator to an outbound frame.
pub fn frame_line(line: &str) -> String {
    format!("{line}{TERMINATOR}")
}

/// The initial `player` handshake frame for a given client type.
pub fn player_frame(client_type: &str) -> String {
    format!(r#"{{"context":"player","data":"{client_type}"}}"#)
}

/// The `protocol` handshake reply advertising the negotiated version.
pub fn protocol_frame(protocol_version: u8, no_broadcast: bool) -> String {
    format!(
        r#"{{"context":"protocol","data":{{"protocol_version":{protocol_version},"no_broadcast":{no_broadcast}}}}}"#
    )
}

/// The `pong` keepalive reply.
pub fn pong_frame() -> String {
    r#"{"context":"pong","data":""}"#.to_string()
}

/// Client-side handshake + keepalive automation. Feed it inbound contexts and it
/// yields the frames a client should send back: the server's `player` echo ->
/// `protocol` (once), and `ping` -> `pong`.
pub struct ClientHandshake {
    client_type: String,
    protocol_version: u8,
    no_broadcast: bool,
    player_acked: bool,
}

impl ClientHandshake {
    pub fn new(client_type: impl Into<String>, protocol_version: u8, no_broadcast: bool) -> Self {
        Self {
            client_type: client_type.into(),
            protocol_version,
            no_broadcast,
            player_acked: false,
        }
    }

    /// The first frame to send on connect.
    pub fn initial(&self) -> String {
        player_frame(&self.client_type)
    }

    /// The auto-reply for an inbound context, if any. `player` yields the
    /// `protocol` frame exactly once; `ping` always yields `pong`.
    pub fn on_incoming(&mut self, context: &str) -> Option<String> {
        match context {
            CTX_PING => Some(pong_frame()),
            CTX_PLAYER if !self.player_acked => {
                self.player_acked = true;
                Some(protocol_frame(self.protocol_version, self.no_broadcast))
            }
            _ => None,
        }
    }
}

/// Splits a byte stream into CRLF-delimited frames, matching the legacy client's
/// strict `\r\n` framing. Bytes are appended with [`push_bytes`](Self::push_bytes)
/// and complete frames drained with [`next_frame`](Self::next_frame).
#[derive(Default)]
pub struct FrameAccumulator {
    acc: String,
}

impl FrameAccumulator {
    pub fn push_bytes(&mut self, bytes: &[u8]) {
        self.acc.push_str(&String::from_utf8_lossy(bytes));
    }

    /// Pop the next complete frame (terminator stripped), or `None` if no full
    /// frame has arrived yet.
    pub fn next_frame(&mut self) -> Option<String> {
        let idx = self.acc.find(TERMINATOR)?;
        let line = self.acc[..idx].to_string();
        self.acc = self.acc[idx + TERMINATOR.len()..].to_string();
        Some(line)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_context() {
        assert_eq!(
            parse_context(r#"{"context":"player","data":"Android"}"#).as_deref(),
            Some("player")
        );
        assert_eq!(parse_context("not json"), None);
        assert_eq!(parse_context(r#"{"data":1}"#), None);
    }

    #[test]
    fn builds_handshake_frames() {
        assert_eq!(player_frame("iOS"), r#"{"context":"player","data":"iOS"}"#);
        assert_eq!(
            protocol_frame(4, false),
            r#"{"context":"protocol","data":{"protocol_version":4,"no_broadcast":false}}"#
        );
        assert_eq!(pong_frame(), r#"{"context":"pong","data":""}"#);
        assert_eq!(frame_line("{}"), "{}\r\n");
    }

    #[test]
    fn handshake_automation() {
        let mut hs = ClientHandshake::new("Android", 4, true);
        assert_eq!(hs.initial(), r#"{"context":"player","data":"Android"}"#);
        // First server `player` -> protocol frame; subsequent ones -> nothing.
        assert_eq!(
            hs.on_incoming("player").as_deref(),
            Some(r#"{"context":"protocol","data":{"protocol_version":4,"no_broadcast":true}}"#)
        );
        assert_eq!(hs.on_incoming("player"), None);
        // ping always answered.
        assert_eq!(
            hs.on_incoming("ping").as_deref(),
            Some(r#"{"context":"pong","data":""}"#)
        );
        assert_eq!(hs.on_incoming("nowplaying"), None);
    }

    #[test]
    fn lenient_recovers_ios_quote_quirk() {
        // Strict-valid JSON is unchanged.
        assert_eq!(
            parse_lenient(r#"{"context":"player","data":"ok"}"#)
                .unwrap()
                .get("context")
                .unwrap(),
            "player"
        );
        // iOS sends \' inside strings, which is invalid JSON; we recover it.
        let v = parse_lenient(r#"{"context":"lyrics","data":"it\'s here"}"#).unwrap();
        assert_eq!(v["data"], "it's here");
        // A real escaped backslash before a quote is preserved, not mangled.
        assert_eq!(sanitize_ios_quotes(r#"a\\b"#), r#"a\\b"#);
        assert_eq!(sanitize_ios_quotes(r#"it\'s"#), "it's");
    }

    #[test]
    fn lenient_recovers_ios_bare_data_quirk() {
        // The iOS v4 bare-identifier bug: `data` is an unquoted word. We quote it
        // instead of dropping the frame, so the context is still dispatched.
        let v = parse_lenient(r#"{"context":"nowplayingposition","data":status}"#).unwrap();
        assert_eq!(v["context"], "nowplayingposition");
        assert_eq!(v["data"], "status");
        // Minimal form.
        assert_eq!(
            parse_lenient(r#"{"data":status}"#).unwrap()["data"],
            "status"
        );

        // Valid literals / numbers / strings / objects are untouched (strict JSON
        // wins first, and the sanitizer would leave them alone anyway).
        assert_eq!(parse_lenient(r#"{"data":true}"#).unwrap()["data"], true);
        assert_eq!(parse_lenient(r#"{"data":false}"#).unwrap()["data"], false);
        assert!(parse_lenient(r#"{"data":null}"#).unwrap()["data"].is_null());
        assert_eq!(parse_lenient(r#"{"data":42}"#).unwrap()["data"], 42);
        assert_eq!(parse_lenient(r#"{"data":"s"}"#).unwrap()["data"], "s");
        // Genuinely broken frames still fail.
        assert!(parse_lenient("not json at all").is_none());
        assert!(parse_lenient(r#"{"context":"x","data":"#).is_none());
    }

    #[test]
    fn accumulator_splits_on_crlf() {
        let mut acc = FrameAccumulator::default();
        acc.push_bytes(b"{\"a\":1}\r\n{\"b\":2}\r");
        assert_eq!(acc.next_frame().as_deref(), Some(r#"{"a":1}"#));
        // Second frame is incomplete (no trailing \n yet).
        assert_eq!(acc.next_frame(), None);
        acc.push_bytes(b"\n");
        assert_eq!(acc.next_frame().as_deref(), Some(r#"{"b":2}"#));
        assert_eq!(acc.next_frame(), None);
    }
}
