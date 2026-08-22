//! The V6 per-connection protocol state machine, kept pure (no IO) like the legacy
//! [`Session`](super::session::Session) so it is unit-testable without sockets:
//! feed it a wire line, get back the frames to send and whether to close.
//!
//! Step 1 is the **spine only** (#118): the strict envelope, the handshake, and one
//! trivial op (`ping`) to prove round-trips. There is no command surface yet.
//!
//! Contract highlights (all from #118 §2-§5):
//! - First frame must be `op:"handshake"` (`id:0`); it carries `protocol_version`,
//!   a required per-install `client_id`, and a `client_type`.
//! - Any non-handshake op before the handshake -> `unauthorized` + close.
//! - Reject bad *structure* (non-object, missing/mistyped envelope fields, unknown
//!   `kind`/`op`); *ignore* unknown additive keys inside `data`.
//! - Success `{id, kind:"response", data}` XOR failure `{id, kind:"response", error}`.

use serde_json::{json, Value};

use mbrc_wire::v6::{self, ClientType, ErrorCode, RequestError};

use super::commands_v6;
use super::session::Outcome;
use crate::cover::store::CoverStore;
use crate::metadata_cache::MetadataCache;
use crate::nowplaying::NowPlayingCache;
use crate::providers::Providers;

/// Per-connection V6 state, built up at the handshake.
#[derive(Debug, Default)]
pub struct V6Session {
    /// Whether the handshake has completed.
    handshaked: bool,
    /// Required per-install client id (session key + caps grouping).
    client_id: String,
    /// Client identification (metadata only; never forks wire behavior).
    client_type: Option<ClientType>,
    /// If true, this connection does not receive event broadcasts (an auxiliary /
    /// command-only socket). Default false = a main connection.
    no_broadcast: bool,
    /// Inbound frame counter, for wire-log correlation.
    frames_in: u64,
}

impl V6Session {
    /// Process one inbound V6 wire line. `providers`/`now_playing` are the same
    /// read/write context the legacy `Session` gets; op handlers use them.
    #[allow(clippy::too_many_arguments)]
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
        tracing::trace!(
            target: "mbrc::wire",
            dir = "c2s",
            proto = "v6",
            seq,
            bytes = line.len(),
            "{}",
            line
        );

        match v6::parse_request(line) {
            Ok(req) => {
                self.handle_request(req, providers, now_playing, cover_store, metadata_cache)
            }
            Err(err) => self.reject(err),
        }
    }

    /// Whether the handshake has completed (cheap; no allocation).
    pub fn is_handshaked(&self) -> bool {
        self.handshaked
    }

    /// Inbound frame count, for the connection post-mortem log.
    pub fn frames_in(&self) -> u64 {
        self.frames_in
    }

    /// Registration metadata once handshaked (`None` before). `is_main` is `false`
    /// in step 1: V6 has no event surface yet, and a V6 socket must never receive
    /// V4-shaped broadcasts. The connection still registers for per-client/per-IP
    /// caps via its required `client_id`.
    pub fn reg_meta(&self) -> Option<super::RegMeta> {
        if !self.handshaked {
            return None;
        }
        Some(super::RegMeta {
            client_id: Some(self.client_id.clone()),
            // A main (event-subscribing) connection unless it opted out. Unlike the
            // spine, V6 now has an event surface (the player domain), so a main V6
            // connection subscribes to the V6 broadcaster.
            is_main: !self.no_broadcast,
            platform: self.client_type.map(|c| c.as_str().to_string()),
            protocol: v6::PROTOCOL_VERSION as u8,
        })
    }

    /// A structural rejection from the envelope parser. Pre-handshake structural
    /// errors are fatal (the connection never established); post-handshake ones are
    /// reported but leave the connection open (lenient continuation).
    fn reject(&self, err: RequestError) -> Outcome {
        let message = match &err {
            RequestError::Malformed => "frame is not a JSON object".to_string(),
            RequestError::MissingField { field, .. } => format!("missing required field: {field}"),
            RequestError::InvalidField { field, .. } => format!("invalid field: {field}"),
        };
        let frame = v6::response_error(err.id(), err.code(), &message);
        if self.handshaked {
            Outcome::reply(frame)
        } else {
            Outcome::reply_and_close(frame)
        }
    }

    #[allow(clippy::too_many_arguments)]
    fn handle_request(
        &mut self,
        req: v6::IncomingRequest,
        providers: &dyn Providers,
        now_playing: Option<&NowPlayingCache>,
        cover_store: Option<&CoverStore>,
        metadata_cache: Option<&MetadataCache>,
    ) -> Outcome {
        if req.op == v6::OP_HANDSHAKE {
            if self.handshaked {
                // A repeat handshake is a protocol-state error (op not permitted in
                // the current state); the connection is not re-negotiated but stays
                // open. `not_allowed` per #118 / docs/protocol-v6.md.
                return Outcome::reply(v6::response_error(
                    req.id,
                    ErrorCode::NotAllowed,
                    "handshake already completed on this connection",
                ));
            }
            return self.handle_handshake(req);
        }

        if !self.handshaked {
            // Any op before the handshake: reject and close so the client
            // re-establishes with a proper handshake first.
            tracing::debug!(op = %req.op, "v6 command before handshake; closing");
            return Outcome::reply_and_close(v6::response_error(
                req.id,
                ErrorCode::Unauthorized,
                "handshake required before any other op",
            ));
        }

        self.dispatch_op(req, providers, now_playing, cover_store, metadata_cache)
    }

    /// Route a post-handshake op: `ping` is built in; everything else goes through
    /// the V6 command catalog. An unrecognized op is `unknown_op`.
    #[allow(clippy::too_many_arguments)]
    fn dispatch_op(
        &mut self,
        req: v6::IncomingRequest,
        providers: &dyn Providers,
        now_playing: Option<&NowPlayingCache>,
        cover_store: Option<&CoverStore>,
        metadata_cache: Option<&MetadataCache>,
    ) -> Outcome {
        // Echo the request data back, proving id-correlated round-trips.
        if req.op == "ping" {
            return Outcome::reply(v6::response_ok(req.id, req.data));
        }
        match commands_v6::dispatch(
            &req.op,
            &req.data,
            providers,
            now_playing,
            cover_store,
            metadata_cache,
        ) {
            Some(Ok(data)) => Outcome::reply(v6::response_ok(req.id, data)),
            Some(Err(e)) => Outcome::reply(v6::response_error(req.id, e.code, &e.message)),
            None => Outcome::reply(v6::response_error(
                req.id,
                ErrorCode::UnknownOp,
                &format!("unknown op: {}", req.op),
            )),
        }
    }

    fn handle_handshake(&mut self, req: v6::IncomingRequest) -> Outcome {
        let data = &req.data;

        // protocol_version: present, numeric, and exactly the version we speak.
        match data.get("protocol_version") {
            None => return self.reject_handshake(ErrorCode::MissingField, "protocol_version"),
            Some(v) => match v.as_u64() {
                Some(n) if n == v6::PROTOCOL_VERSION => {}
                Some(_) => {
                    return self.reject_handshake(ErrorCode::UnsupportedVersion, "protocol_version")
                }
                None => return self.reject_handshake(ErrorCode::InvalidField, "protocol_version"),
            },
        }

        // client_id: required, a non-empty string.
        let client_id = match data.get("client_id") {
            None => return self.reject_handshake(ErrorCode::MissingField, "client_id"),
            Some(Value::String(s)) if !s.is_empty() => s.clone(),
            Some(_) => return self.reject_handshake(ErrorCode::InvalidField, "client_id"),
        };

        // client_type: required, a known enum value.
        let client_type = match data.get("client_type") {
            None => return self.reject_handshake(ErrorCode::MissingField, "client_type"),
            Some(Value::String(s)) => match ClientType::parse(s) {
                Some(ct) => ct,
                None => return self.reject_handshake(ErrorCode::InvalidField, "client_type"),
            },
            Some(_) => return self.reject_handshake(ErrorCode::InvalidField, "client_type"),
        };

        // Optional event opt-out (default false = a main connection). Absent or
        // wrong-typed -> false, per the ignore-unknown-additive-fields policy.
        let no_broadcast = data
            .get("no_broadcast")
            .and_then(Value::as_bool)
            .unwrap_or(false);

        self.handshaked = true;
        self.client_id = client_id;
        self.client_type = Some(client_type);
        self.no_broadcast = no_broadcast;
        tracing::debug!(
            client_type = client_type.as_str(),
            no_broadcast,
            "v6 handshake complete"
        );
        // Advertise the op/event surface so a client can degrade gracefully.
        // Additive (#118 §9 Q5); older clients ignore it.
        Outcome::reply(v6::response_ok(
            0,
            json!({
                "server_version": v6::PROTOCOL_VERSION,
                "capabilities": commands_v6::capabilities(),
            }),
        ))
    }

    /// A handshake validation failure: reply a typed error (echoing id 0) and close,
    /// since the connection never established.
    fn reject_handshake(&self, code: ErrorCode, field: &str) -> Outcome {
        let message = match code {
            ErrorCode::UnsupportedVersion => {
                format!(
                    "unsupported {field}; this server speaks {}",
                    v6::PROTOCOL_VERSION
                )
            }
            ErrorCode::MissingField => format!("missing required field: {field}"),
            _ => format!("invalid field: {field}"),
        };
        tracing::info!(field, code = code.as_str(), "rejecting v6 handshake");
        Outcome::reply_and_close(v6::response_error(0, code, &message))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::providers::NullProviders;
    use serde_json::Value;

    fn parse(frame: &str) -> Value {
        serde_json::from_str(frame).expect("reply is JSON")
    }

    /// Drive one frame through the session with a no-op provider context (the spine
    /// ops here don't touch providers; op-handler behavior is tested in `commands_v6`).
    fn feed(s: &mut V6Session, line: &str) -> Outcome {
        s.handle_frame(line, &NullProviders, None, None, None)
    }

    const GOOD_HANDSHAKE: &str = r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"install-1","client_type":"android"}}"#;

    #[test]
    fn handshake_is_accepted_and_reports_server_version() {
        let mut s = V6Session::default();
        let out = feed(&mut s, GOOD_HANDSHAKE);
        assert!(!out.close);
        assert!(s.handshaked);
        let v = parse(&out.replies[0]);
        assert_eq!(v["id"], 0);
        assert_eq!(v["kind"], "response");
        assert_eq!(v["data"]["server_version"], 6);
        // Registration metadata is exposed post-handshake; a default handshake (no
        // no_broadcast) is a main connection.
        let meta = s.reg_meta().unwrap();
        assert_eq!(meta.client_id.as_deref(), Some("install-1"));
        assert!(meta.is_main);
        assert_eq!(meta.protocol, 6);
    }

    #[test]
    fn no_broadcast_handshake_is_not_a_main() {
        let mut s = V6Session::default();
        feed(
            &mut s,
            r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"x","client_type":"android","no_broadcast":true}}"#,
        );
        assert!(!s.reg_meta().unwrap().is_main);
    }

    #[test]
    fn ping_round_trips_and_echoes_data() {
        let mut s = V6Session::default();
        feed(&mut s, GOOD_HANDSHAKE);
        let out = feed(
            &mut s,
            r#"{"id":1,"kind":"request","op":"ping","data":{"n":42}}"#,
        );
        assert!(!out.close);
        let v = parse(&out.replies[0]);
        assert_eq!(v["id"], 1);
        assert_eq!(v["kind"], "response");
        assert_eq!(v["data"]["n"], 42);
    }

    #[test]
    fn missing_client_id_is_missing_field_and_closes() {
        let mut s = V6Session::default();
        let out = feed(
            &mut s,
            r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_type":"ios"}}"#,
        );
        assert!(out.close, "a rejected handshake closes the connection");
        assert!(!s.handshaked);
        let v = parse(&out.replies[0]);
        assert_eq!(v["error"]["code"], "missing_field");
    }

    #[test]
    fn empty_client_id_is_invalid_field() {
        let mut s = V6Session::default();
        let out = feed(
            &mut s,
            r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"","client_type":"ios"}}"#,
        );
        assert!(out.close);
        assert_eq!(parse(&out.replies[0])["error"]["code"], "invalid_field");
    }

    #[test]
    fn wrong_version_is_unsupported_version() {
        let mut s = V6Session::default();
        let out = feed(
            &mut s,
            r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":5,"client_id":"x","client_type":"ios"}}"#,
        );
        assert!(out.close);
        assert_eq!(
            parse(&out.replies[0])["error"]["code"],
            "unsupported_version"
        );
    }

    #[test]
    fn unknown_client_type_is_invalid_field() {
        let mut s = V6Session::default();
        let out = feed(
            &mut s,
            r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"x","client_type":"toaster"}}"#,
        );
        assert!(out.close);
        assert_eq!(parse(&out.replies[0])["error"]["code"], "invalid_field");
    }

    #[test]
    fn op_before_handshake_is_unauthorized_and_closes() {
        let mut s = V6Session::default();
        let out = feed(&mut s, r#"{"id":1,"kind":"request","op":"ping","data":{}}"#);
        assert!(out.close);
        assert_eq!(parse(&out.replies[0])["error"]["code"], "unauthorized");
    }

    #[test]
    fn second_handshake_is_rejected_without_closing() {
        let mut s = V6Session::default();
        feed(&mut s, GOOD_HANDSHAKE);
        let out = feed(&mut s, GOOD_HANDSHAKE);
        assert!(
            !out.close,
            "a repeat handshake does not tear down the session"
        );
        assert_eq!(parse(&out.replies[0])["error"]["code"], "not_allowed");
        assert!(s.handshaked);
    }

    #[test]
    fn unknown_op_after_handshake_is_unknown_op() {
        let mut s = V6Session::default();
        feed(&mut s, GOOD_HANDSHAKE);
        let out = feed(
            &mut s,
            r#"{"id":2,"kind":"request","op":"teleport","data":{}}"#,
        );
        assert!(!out.close);
        assert_eq!(parse(&out.replies[0])["error"]["code"], "unknown_op");
    }

    #[test]
    fn unknown_data_key_is_ignored() {
        // An unknown additive key in the handshake data is accepted (structure is
        // valid; only unknown *structure* is rejected).
        let mut s = V6Session::default();
        let out = feed(
            &mut s,
            r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"x","client_type":"cli","future_flag":true}}"#,
        );
        assert!(!out.close);
        assert!(s.handshaked);
    }

    #[test]
    fn malformed_pre_handshake_frame_closes() {
        let mut s = V6Session::default();
        let out = feed(&mut s, "not json");
        assert!(out.close);
        assert_eq!(parse(&out.replies[0])["error"]["code"], "malformed_frame");
    }
}
