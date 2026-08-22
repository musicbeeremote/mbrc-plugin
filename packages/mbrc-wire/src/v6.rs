//! V6 wire protocol primitives: the strict JSON envelope, defined string enums,
//! newline framing, and client/server frame builders. Shared by the core server,
//! the `mbrc` CLI, and the api-debugger backend so the envelope has a single
//! definition.
//!
//! V6 runs parallel to the legacy V4/V5 protocol (see the crate root) on the same
//! port; a peer routes by the first frame's shape. Where legacy is
//! `{"context":..,"data":..}`, CRLF-delimited, V6 is
//! `{"id":N,"kind":"request","op":..,"data":..}`, newline-delimited, with typed
//! `response`/`error` replies. See MBRCIP-0003 (issue #118).

use serde::{Deserialize, Serialize};
use serde_json::{json, Map, Value};

/// V6 line terminator: a bare newline (legacy is CRLF).
pub const TERMINATOR: &str = "\n";

/// The V6 protocol major version this crate speaks.
pub const PROTOCOL_VERSION: u64 = 6;

/// The handshake op name - the first frame on every V6 connection.
pub const OP_HANDSHAKE: &str = "handshake";

/// Envelope discriminator. Every V6 frame is one of these.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum Kind {
    Request,
    Response,
    Event,
}

/// Client identification (metadata only - never forks wire behavior, per #118 §7).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ClientType {
    Android,
    Ios,
    Desktop,
    Web,
    Cli,
}

impl ClientType {
    /// The on-wire string for this client type.
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Android => "android",
            Self::Ios => "ios",
            Self::Desktop => "desktop",
            Self::Web => "web",
            Self::Cli => "cli",
        }
    }

    /// Parse an on-wire client-type string (exact match, snake_case).
    pub fn parse(s: &str) -> Option<Self> {
        match s {
            "android" => Some(Self::Android),
            "ios" => Some(Self::Ios),
            "desktop" => Some(Self::Desktop),
            "web" => Some(Self::Web),
            "cli" => Some(Self::Cli),
            _ => None,
        }
    }
}

/// The closed set of framing/handshake error codes (step 1). Op-specific codes are
/// added by later steps.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ErrorCode {
    /// Invalid JSON, or a frame that is not a JSON object.
    MalformedFrame,
    /// The handshake `protocol_version` is not one this server speaks.
    UnsupportedVersion,
    /// A required field is absent.
    MissingField,
    /// A field is present but the wrong type or an unknown enum value.
    InvalidField,
    /// The `op` is not a known operation.
    UnknownOp,
    /// A request arrived before the handshake completed.
    Unauthorized,
    /// The connection is not permitted (address filter / connection cap).
    NotAllowed,
    /// A server-side failure carrying out the op (e.g. a MusicBee FFI error).
    Internal,
    /// The requested resource (track, cover, ...) does not exist.
    NotFound,
    /// The action is unavailable in the current configuration (e.g. scrobbling
    /// with no last.fm account) - a precondition, not an internal failure.
    Unavailable,
}

impl ErrorCode {
    /// The on-wire string for this code.
    pub fn as_str(self) -> &'static str {
        match self {
            Self::MalformedFrame => "malformed_frame",
            Self::UnsupportedVersion => "unsupported_version",
            Self::MissingField => "missing_field",
            Self::InvalidField => "invalid_field",
            Self::UnknownOp => "unknown_op",
            Self::Unauthorized => "unauthorized",
            Self::NotAllowed => "not_allowed",
            Self::Internal => "internal_error",
            Self::NotFound => "not_found",
            Self::Unavailable => "unavailable",
        }
    }
}

/// Append the V6 line terminator to an outbound frame body.
pub fn frame_line(line: &str) -> String {
    format!("{line}{TERMINATOR}")
}

// --- Outbound frame builders (bodies only; the IO layer adds the terminator) ---

/// A success response echoing the request `id` and carrying `data`.
pub fn response_ok(id: u64, data: Value) -> String {
    serde_json::to_string(&json!({ "id": id, "kind": "response", "data": data }))
        .expect("serializing a V6 response cannot fail")
}

/// A failure response echoing the request `id` and carrying a typed `error`.
pub fn response_error(id: u64, code: ErrorCode, message: &str) -> String {
    serde_json::to_string(&json!({
        "id": id,
        "kind": "response",
        "error": { "code": code.as_str(), "message": message },
    }))
    .expect("serializing a V6 error cannot fail")
}

/// An unsolicited event frame (no `id`).
pub fn event(event: &str, data: Value) -> String {
    serde_json::to_string(&json!({ "kind": "event", "event": event, "data": data }))
        .expect("serializing a V6 event cannot fail")
}

/// A generic request frame (client side).
pub fn request(id: u64, op: &str, data: Value) -> String {
    serde_json::to_string(&json!({ "id": id, "kind": "request", "op": op, "data": data }))
        .expect("serializing a V6 request cannot fail")
}

/// The handshake request frame (client side): `op:"handshake"`, `id:0`.
///
/// `no_broadcast` opts the connection out of event delivery (an auxiliary /
/// command-only socket); a main connection sends `false`.
pub fn handshake_request(client_id: &str, client_type: ClientType, no_broadcast: bool) -> String {
    request(
        0,
        OP_HANDSHAKE,
        json!({
            "protocol_version": PROTOCOL_VERSION,
            "client_id": client_id,
            "client_type": client_type.as_str(),
            "no_broadcast": no_broadcast,
        }),
    )
}

// --- Inbound request parsing (server side) ---

/// A structurally valid inbound request. `data` is passed through untyped for the
/// op handler / handshake validator (unknown additive `data` keys are ignored per
/// #118 §2.1).
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct IncomingRequest {
    pub id: u64,
    pub op: String,
    pub data: Value,
}

/// Why an inbound request envelope was rejected. Each maps to an [`ErrorCode`].
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum RequestError {
    /// Not JSON, or not a JSON object. No `id` recoverable -> responses use id 0.
    Malformed,
    /// A required envelope field (`id`, `kind`, `op`) is absent. Carries the id if
    /// one was still readable, for response correlation.
    MissingField { id: u64, field: &'static str },
    /// A present envelope field has the wrong type or value (`kind != "request"`,
    /// `id` not a u64).
    InvalidField { id: u64, field: &'static str },
}

impl RequestError {
    /// The [`ErrorCode`] to report on the wire for this rejection.
    pub fn code(&self) -> ErrorCode {
        match self {
            Self::Malformed => ErrorCode::MalformedFrame,
            Self::MissingField { .. } => ErrorCode::MissingField,
            Self::InvalidField { .. } => ErrorCode::InvalidField,
        }
    }

    /// The request `id` to echo on the error response (0 when unrecoverable).
    pub fn id(&self) -> u64 {
        match self {
            Self::Malformed => 0,
            Self::MissingField { id, .. } | Self::InvalidField { id, .. } => *id,
        }
    }
}

/// Parse and structurally validate an inbound request line. Enforces the V6
/// envelope shape (a JSON object with a numeric `id`, `kind == "request"`, and a
/// string `op`); `data` defaults to `null` when absent. Unknown extra envelope
/// keys are ignored (only structure is validated), matching #118 §2.1.
pub fn parse_request(line: &str) -> Result<IncomingRequest, RequestError> {
    let value: Value = serde_json::from_str(line).map_err(|_| RequestError::Malformed)?;
    let obj: &Map<String, Value> = value.as_object().ok_or(RequestError::Malformed)?;

    // `id` first, so later errors can echo it for correlation.
    let id = match obj.get("id") {
        None => return Err(RequestError::MissingField { id: 0, field: "id" }),
        Some(v) => v
            .as_u64()
            .ok_or(RequestError::InvalidField { id: 0, field: "id" })?,
    };

    match obj.get("kind").and_then(Value::as_str) {
        None => return Err(RequestError::MissingField { id, field: "kind" }),
        Some("request") => {}
        Some(_) => return Err(RequestError::InvalidField { id, field: "kind" }),
    }

    let op = match obj.get("op") {
        None => return Err(RequestError::MissingField { id, field: "op" }),
        Some(Value::String(s)) => s.clone(),
        Some(_) => return Err(RequestError::InvalidField { id, field: "op" }),
    };

    let data = obj.get("data").cloned().unwrap_or(Value::Null);
    Ok(IncomingRequest { id, op, data })
}

// --- Inbound response parsing (client side: CLI / debugger) ---

/// A typed error carried on a failure response.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct WireError {
    pub code: String,
    pub message: String,
}

/// A parsed server response: the echoed `id` and either the success `data` or a
/// typed error.
#[derive(Debug, Clone, PartialEq)]
pub struct IncomingResponse {
    pub id: u64,
    pub result: Result<Value, WireError>,
}

/// Parse a server-sent line as a V6 response. Returns `None` for a non-object, a
/// frame whose `kind` is not `"response"` (e.g. an event), or a missing `id`.
pub fn parse_response(line: &str) -> Option<IncomingResponse> {
    let value: Value = serde_json::from_str(line).ok()?;
    let obj = value.as_object()?;
    if obj.get("kind").and_then(Value::as_str) != Some("response") {
        return None;
    }
    let id = obj.get("id").and_then(Value::as_u64)?;
    let result = match obj.get("error") {
        Some(err) => Err(WireError {
            code: err
                .get("code")
                .and_then(Value::as_str)
                .unwrap_or_default()
                .to_string(),
            message: err
                .get("message")
                .and_then(Value::as_str)
                .unwrap_or_default()
                .to_string(),
        }),
        None => Ok(obj.get("data").cloned().unwrap_or(Value::Null)),
    };
    Some(IncomingResponse { id, result })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn enums_round_trip_snake_case() {
        assert_eq!(ClientType::Ios.as_str(), "ios");
        assert_eq!(ClientType::parse("android"), Some(ClientType::Android));
        assert_eq!(ClientType::parse("windows"), None);
        assert_eq!(
            ErrorCode::UnsupportedVersion.as_str(),
            "unsupported_version"
        );
        // serde spelling matches the manual as_str spelling.
        assert_eq!(
            serde_json::to_string(&Kind::Request).unwrap(),
            "\"request\""
        );
        assert_eq!(serde_json::to_string(&ClientType::Ios).unwrap(), "\"ios\"");
    }

    #[test]
    fn handshake_request_is_well_formed() {
        let frame = handshake_request("dev-uuid", ClientType::Cli, false);
        let v: Value = serde_json::from_str(&frame).unwrap();
        assert_eq!(v["id"], 0);
        assert_eq!(v["kind"], "request");
        assert_eq!(v["op"], "handshake");
        assert_eq!(v["data"]["protocol_version"], 6);
        assert_eq!(v["data"]["client_id"], "dev-uuid");
        assert_eq!(v["data"]["client_type"], "cli");
        assert_eq!(v["data"]["no_broadcast"], false);
    }

    #[test]
    fn response_builders_shape() {
        let ok = response_ok(7, json!({ "volume": 72 }));
        let v: Value = serde_json::from_str(&ok).unwrap();
        assert_eq!(v["id"], 7);
        assert_eq!(v["kind"], "response");
        assert_eq!(v["data"]["volume"], 72);
        assert!(v.get("error").is_none());

        let err = response_error(7, ErrorCode::MissingField, "client_id is required");
        let v: Value = serde_json::from_str(&err).unwrap();
        assert_eq!(v["kind"], "response");
        assert_eq!(v["error"]["code"], "missing_field");
        assert!(v.get("data").is_none());
    }

    #[test]
    fn parse_request_accepts_valid_and_ignores_extra_keys() {
        let req =
            parse_request(r#"{"id":3,"kind":"request","op":"ping","data":{"x":1},"trace":"ok"}"#)
                .unwrap();
        assert_eq!(req.id, 3);
        assert_eq!(req.op, "ping");
        assert_eq!(req.data["x"], 1);
        // data defaults to null when absent.
        let req = parse_request(r#"{"id":4,"kind":"request","op":"ping"}"#).unwrap();
        assert_eq!(req.data, Value::Null);
    }

    #[test]
    fn parse_request_rejects_bad_structure() {
        assert_eq!(parse_request("not json"), Err(RequestError::Malformed));
        assert_eq!(parse_request("[1,2,3]"), Err(RequestError::Malformed));
        assert_eq!(
            parse_request(r#"{"kind":"request","op":"ping"}"#),
            Err(RequestError::MissingField { id: 0, field: "id" })
        );
        assert_eq!(
            parse_request(r#"{"id":"x","kind":"request","op":"ping"}"#),
            Err(RequestError::InvalidField { id: 0, field: "id" })
        );
        assert_eq!(
            parse_request(r#"{"id":1,"op":"ping"}"#),
            Err(RequestError::MissingField {
                id: 1,
                field: "kind"
            })
        );
        assert_eq!(
            parse_request(r#"{"id":1,"kind":"response","op":"ping"}"#),
            Err(RequestError::InvalidField {
                id: 1,
                field: "kind"
            })
        );
        assert_eq!(
            parse_request(r#"{"id":1,"kind":"request"}"#),
            Err(RequestError::MissingField { id: 1, field: "op" })
        );
    }

    #[test]
    fn parse_response_success_and_error() {
        let ok =
            parse_response(r#"{"id":0,"kind":"response","data":{"server_version":6}}"#).unwrap();
        assert_eq!(ok.id, 0);
        assert_eq!(ok.result.unwrap()["server_version"], 6);

        let err = parse_response(
            r#"{"id":1,"kind":"response","error":{"code":"unknown_op","message":"nope"}}"#,
        )
        .unwrap();
        assert_eq!(err.result.unwrap_err().code, "unknown_op");

        // An event is not a response.
        assert!(parse_response(r#"{"kind":"event","event":"x","data":{}}"#).is_none());
    }
}
