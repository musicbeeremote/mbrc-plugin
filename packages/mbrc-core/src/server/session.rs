//! The per-connection protocol state machine, kept pure (no IO) so it is
//! unit-testable without sockets: feed it a wire line, get back the frames to
//! send and whether to close. The IO layer (`connection.rs`) does the reading
//! and writing.
//!
//! Slice 1 handles the handshake (`player`, `protocol`) and keepalive/health
//! (`ping`, `pong`, `verifyconnection`). Command dispatch for every other
//! context lands in Slice 2.

use serde_json::{json, Value};

use mbrc_wire::parse_lenient;

use super::commands;
use crate::cover::store::CoverStore;
use crate::metadata_cache::MetadataCache;
use crate::nowplaying::NowPlayingCache;
use crate::protocol::version::ProtocolVersion;
use crate::providers::Providers;

/// Protocol version this server speaks.
pub const SERVER_PROTOCOL: u8 = 4;
/// Minimum client protocol accepted; older clients are rejected at handshake.
pub const MIN_PROTOCOL: u8 = 4;
/// The identity the plugin reports for the `player` context.
pub const PLAYER_NAME: &str = "MusicBee";

/// Per-connection state built up during and after the handshake.
#[derive(Debug, Default)]
pub struct Session {
    /// Client platform from the `player` frame (`"Android"` / `"iOS"`).
    pub platform: Option<String>,
    /// Negotiated protocol version (set once `protocol` is accepted).
    pub protocol_version: Option<u8>,
    /// If true, this connection does not receive broadcasts.
    pub no_broadcast: bool,
    /// Client-provided identifier from the `protocol` handshake, when present.
    /// Android v4 sends a UUID; iOS and old Android send none. Distinct from the
    /// server-assigned connection id; used to group a client's sockets (caps +
    /// supersession).
    pub client_id: Option<String>,
    /// Count of inbound frames seen on this connection; also the `seq` stamped
    /// on each wire line so frame order is explicit (mirrors the C# packet no.).
    pub frames_in: u64,
    /// Commands dropped because the handshake wasn't complete. A non-zero value
    /// at close is the signature of a client control/event socket that never
    /// handshaked.
    pub dropped_pre_handshake: u32,
}

/// What the IO layer should do with a frame: which raw JSON replies to send
/// (it applies CRLF framing) and whether to close afterward.
#[derive(Debug, Default, PartialEq, Eq)]
pub struct Outcome {
    pub replies: Vec<String>,
    pub close: bool,
}

impl Outcome {
    fn nothing() -> Self {
        Self::default()
    }
    fn reply(frame: String) -> Self {
        Self {
            replies: vec![frame],
            close: false,
        }
    }
    fn reply_and_close(frame: String) -> Self {
        Self {
            replies: vec![frame],
            close: true,
        }
    }
    /// Close the connection without sending a frame.
    fn close() -> Self {
        Self {
            replies: Vec::new(),
            close: true,
        }
    }
}

impl Session {
    /// Process one inbound wire line. Handshake/keepalive contexts are handled
    /// here; other contexts are dispatched to command handlers (once the
    /// handshake is complete). Unparseable frames (including the iOS
    /// bare-identifier quirk lenient parsing can't recover) are dropped without
    /// closing the connection.
    pub fn handle_frame(
        &mut self,
        line: &str,
        providers: &dyn Providers,
        now_playing: Option<&NowPlayingCache>,
        cover_store: Option<&CoverStore>,
        metadata_cache: Option<&MetadataCache>,
    ) -> Outcome {
        let seq = self.frames_in;
        self.frames_in += 1;
        let Some(value) = parse_lenient(line) else {
            tracing::debug!(
                target: "mbrc::wire",
                dir = "c2s",
                seq,
                bytes = line.len(),
                parseable = false,
                platform = self.platform.as_deref().unwrap_or("none"),
                handshaken = self.protocol_version.is_some(),
                "dropping unparseable frame: {}",
                crate::logging::redact_frame(line, None)
            );
            return Outcome::nothing();
        };
        let context = value.get("context").and_then(Value::as_str).unwrap_or("");
        let data = value.get("data").cloned().unwrap_or(Value::Null);
        // DEBUG caps list bodies to a sample + schema summary; TRACE keeps the
        // full body. Only one fires (TRACE implies DEBUG), so a busy list frame
        // isn't logged twice.
        if tracing::enabled!(target: "mbrc::wire", tracing::Level::TRACE) {
            tracing::trace!(
                target: "mbrc::wire",
                dir = "c2s",
                seq,
                context,
                bytes = line.len(),
                "{}",
                crate::logging::redact_frame(line, None)
            );
        } else {
            tracing::debug!(
                target: "mbrc::wire",
                dir = "c2s",
                seq,
                context,
                bytes = line.len(),
                "{}",
                crate::logging::redact_frame(line, Some(crate::logging::WIRE_LIST_SAMPLE))
            );
        }

        match context {
            "player" => {
                if let Some(platform) = data.as_str() {
                    self.platform = Some(platform.to_string());
                    tracing::debug!(platform, "handshake: player identity recorded");
                }
                Outcome::reply(frame("player", json!(PLAYER_NAME)))
            }
            "protocol" => self.handle_protocol(&data),
            "ping" => Outcome::reply(frame("pong", json!(""))),
            "pong" => Outcome::nothing(),
            // Answered before any auth/dispatch gate, per the V4 contract.
            "verifyconnection" => Outcome::reply(frame("verifyconnection", json!(""))),
            "" => Outcome::nothing(),
            other => self.dispatch_command(
                other,
                &data,
                providers,
                now_playing,
                cover_store,
                metadata_cache,
                seq,
            ),
        }
    }

    /// Route a command context to its handler and frame the replies. Commands
    /// received before the handshake completes are ignored.
    #[allow(clippy::too_many_arguments)]
    fn dispatch_command(
        &mut self,
        context: &str,
        data: &Value,
        providers: &dyn Providers,
        now_playing: Option<&NowPlayingCache>,
        cover_store: Option<&CoverStore>,
        metadata_cache: Option<&MetadataCache>,
        seq: u64,
    ) -> Outcome {
        let Some(negotiated) = self.protocol_version else {
            self.dropped_pre_handshake += 1;
            // A command before the handshake completed: force-close the socket so
            // the client re-establishes with a proper `player`/`protocol` sequence.
            // This matches the shipped C# `ProtocolHandler` (which
            // `ForceClientDisconnect`s any socket whose first frame isn't `player`).
            // iOS reuses a command socket and, after a server-side close/restart,
            // reconnects WITHOUT re-handshaking - silently dropping its controls
            // left the app stuck; closing kicks it back into a handshake.
            // platform tells the two failure modes apart: "iOS"/"Android" means
            // the socket sent `player` but never a valid `protocol`; "none" means a
            // command-only socket that skipped the handshake entirely.
            tracing::debug!(
                seq,
                context,
                platform = self.platform.as_deref().unwrap_or("none"),
                "command before handshake; closing to force a re-handshake"
            );
            return Outcome::close();
        };
        // The handshake only accepts versions with a formatter; default to V4
        // defensively so a command is never dropped over a version lookup.
        let version = ProtocolVersion::from_negotiated(negotiated).unwrap_or(ProtocolVersion::V4);
        let platform = commands::Platform::from_name(self.platform.as_deref());
        let mut ctx = commands::Ctx::new(providers, version).with_platform(platform);
        if let Some(cache) = now_playing {
            ctx = ctx.with_now_playing(cache);
        }
        if let Some(store) = cover_store {
            ctx = ctx.with_cover_store(store);
        }
        if let Some(cache) = metadata_cache {
            ctx = ctx.with_metadata_cache(cache);
        }
        match commands::dispatch(&ctx, context, data) {
            Some(Ok(replies)) => {
                let framed: Vec<String> = replies.into_iter().map(|(c, d)| frame(&c, d)).collect();
                for f in &framed {
                    // s2c list/browse responses are the verbose ones: DEBUG caps
                    // the body to a sample + schema; TRACE keeps it whole.
                    if tracing::enabled!(target: "mbrc::wire", tracing::Level::TRACE) {
                        tracing::trace!(
                            target: "mbrc::wire",
                            dir = "s2c",
                            reply_to = seq,
                            bytes = f.len(),
                            "{}",
                            crate::logging::redact_frame(f, None)
                        );
                    } else {
                        tracing::debug!(
                            target: "mbrc::wire",
                            dir = "s2c",
                            reply_to = seq,
                            bytes = f.len(),
                            "{}",
                            crate::logging::redact_frame(f, Some(crate::logging::WIRE_LIST_SAMPLE))
                        );
                    }
                }
                Outcome {
                    replies: framed,
                    close: false,
                }
            }
            Some(Err(e)) => {
                tracing::warn!(context, error = %e, "command handler error");
                Outcome::nothing()
            }
            None => {
                tracing::trace!(context, "no handler for context");
                Outcome::nothing()
            }
        }
    }

    fn handle_protocol(&mut self, data: &Value) -> Outcome {
        let handshake = parse_handshake(data);
        if handshake.version < MIN_PROTOCOL {
            let message = format!(
                "Protocol version {} is not supported. Minimum: {MIN_PROTOCOL}. \
                 Please update your MBRC client.",
                handshake.version
            );
            tracing::info!(
                version = handshake.version,
                "rejecting pre-V{MIN_PROTOCOL} client"
            );
            Outcome::reply_and_close(frame("notallowed", json!(message)))
        } else {
            self.protocol_version = Some(handshake.version);
            self.no_broadcast = handshake.no_broadcast;
            self.client_id = handshake.client_id;
            Outcome::reply(frame("protocol", json!(SERVER_PROTOCOL)))
        }
    }
}

/// The negotiated handshake fields.
struct Handshake {
    version: u8,
    no_broadcast: bool,
    client_id: Option<String>,
}

/// Parse the `protocol` handshake payload, tolerating the legacy bare-int and
/// bare-string forms as well as the V3+ object form. `client_id` and
/// `no_broadcast` only exist in the object form (Android v4); the bare forms
/// (iOS, legacy) yield `None`/`false`.
fn parse_handshake(data: &Value) -> Handshake {
    match data {
        Value::Number(n) => Handshake {
            version: n.as_u64().unwrap_or(0) as u8,
            no_broadcast: false,
            client_id: None,
        },
        Value::String(s) => Handshake {
            version: s.trim().parse().unwrap_or(0),
            no_broadcast: false,
            client_id: None,
        },
        Value::Object(o) => Handshake {
            version: o
                .get("protocol_version")
                .and_then(Value::as_u64)
                .unwrap_or(0) as u8,
            no_broadcast: o
                .get("no_broadcast")
                .and_then(Value::as_bool)
                .unwrap_or(false),
            client_id: o
                .get("client_id")
                .and_then(Value::as_str)
                .filter(|s| !s.is_empty())
                .map(str::to_string),
        },
        _ => Handshake {
            version: 0,
            no_broadcast: false,
            client_id: None,
        },
    }
}

/// Build a raw `{"context":..,"data":..}` frame (the IO layer adds CRLF).
fn frame(context: &str, data: Value) -> String {
    serde_json::to_string(&json!({ "context": context, "data": data }))
        .expect("serializing a server frame cannot fail")
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::providers::NullProviders;

    fn ctx(line: &str) -> (String, Value) {
        let v: Value = serde_json::from_str(line).unwrap();
        (
            v["context"].as_str().unwrap().to_string(),
            v["data"].clone(),
        )
    }

    #[test]
    fn player_records_platform_and_replies_identity() {
        let mut s = Session::default();
        let out = s.handle_frame(
            r#"{"context":"player","data":"Android"}"#,
            &NullProviders,
            None,
            None,
            None,
        );
        assert_eq!(s.platform.as_deref(), Some("Android"));
        assert!(!out.close);
        assert_eq!(out.replies.len(), 1);
        let (c, d) = ctx(&out.replies[0]);
        assert_eq!(c, "player");
        assert_eq!(d, json!("MusicBee"));
    }

    #[test]
    fn protocol_v4_object_is_accepted() {
        let mut s = Session::default();
        let out = s.handle_frame(
            r#"{"context":"protocol","data":{"protocol_version":4,"no_broadcast":true}}"#,
            &NullProviders,
            None,
            None,
            None,
        );
        assert_eq!(s.protocol_version, Some(4));
        assert!(s.no_broadcast);
        assert_eq!(s.client_id, None); // absent in this handshake
        assert!(!out.close);
        let (c, d) = ctx(&out.replies[0]);
        assert_eq!(c, "protocol");
        assert_eq!(d, json!(4));
    }

    #[test]
    fn client_id_parsed_from_object_and_absent_for_bare_int() {
        // Android v4 sends a client_id in the object handshake.
        let mut s = Session::default();
        s.handle_frame(
            r#"{"context":"protocol","data":{"client_id":"abc-123","no_broadcast":false,"protocol_version":4}}"#,
            &NullProviders,
            None,
            None,
            None,
        );
        assert_eq!(s.client_id.as_deref(), Some("abc-123"));
        assert!(!s.no_broadcast);

        // iOS sends the bare int - no client_id.
        let mut ios = Session::default();
        ios.handle_frame(
            r#"{"context":"protocol","data":4}"#,
            &NullProviders,
            None,
            None,
            None,
        );
        assert_eq!(ios.protocol_version, Some(4));
        assert_eq!(ios.client_id, None);
    }

    #[test]
    fn pre_v4_is_rejected_and_closed() {
        for line in [
            r#"{"context":"protocol","data":3}"#,
            r#"{"context":"protocol","data":"2"}"#,
            r#"{"context":"protocol","data":{"protocol_version":3}}"#,
        ] {
            let mut s = Session::default();
            let out = s.handle_frame(line, &NullProviders, None, None, None);
            assert!(out.close, "should close for {line}");
            assert_eq!(s.protocol_version, None);
            let (c, _) = ctx(&out.replies[0]);
            assert_eq!(c, "notallowed");
        }
    }

    #[test]
    fn ping_replies_pong_and_verifyconnection_echoes() {
        let mut s = Session::default();
        let (c, d) = ctx(&s
            .handle_frame(
                r#"{"context":"ping","data":""}"#,
                &NullProviders,
                None,
                None,
                None,
            )
            .replies[0]);
        assert_eq!((c.as_str(), d), ("pong", json!("")));

        let (c, d) = ctx(&s
            .handle_frame(
                r#"{"context":"verifyconnection","data":""}"#,
                &NullProviders,
                None,
                None,
                None,
            )
            .replies[0]);
        assert_eq!((c.as_str(), d), ("verifyconnection", json!("")));
    }

    #[test]
    fn pong_and_malformed_are_ignored_without_closing() {
        let mut s = Session::default();
        // pong is handled before dispatch (never a command), so it's ignored.
        assert_eq!(
            s.handle_frame(
                r#"{"context":"pong","data":true}"#,
                &NullProviders,
                None,
                None,
                None
            ),
            Outcome::nothing()
        );
        // Not JSON at all: unparseable, dropped, no close (a parse failure isn't a
        // pre-handshake command).
        assert_eq!(
            s.handle_frame("this is not json", &NullProviders, None, None, None),
            Outcome::nothing()
        );
    }

    #[test]
    fn command_before_handshake_closes_to_force_rehandshake() {
        // A command sent before `player`/`protocol` completes force-closes the
        // socket (matches C# `ForceClientDisconnect`), so the client re-handshakes.
        let mut s = Session::default();
        assert_eq!(
            s.handle_frame(
                r#"{"context":"playernext","data":true}"#,
                &NullProviders,
                None,
                None,
                None
            ),
            Outcome::close()
        );
        // The recovered iOS bare-identifier quirk parses to a real command, so it
        // too force-closes when it arrives pre-handshake.
        let mut s2 = Session::default();
        assert_eq!(
            s2.handle_frame(
                r#"{"context":"nowplayingposition","data":status}"#,
                &NullProviders,
                None,
                None,
                None
            ),
            Outcome::close()
        );
        assert_eq!(s2.dropped_pre_handshake, 1);
    }
}
