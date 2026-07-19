//! First-frame protocol routing. V6 runs parallel to the frozen V4/V5 protocol on
//! the same port (#118 §6); both are newline-JSON, so a connection is routed by the
//! *shape* of its opening frame, not the first byte.
//!
//! - Legacy V4/V5 opens `{"context":"player",...}` - a string `context` key.
//! - V6 opens the handshake `{"kind":"request","op":"handshake",...}` - a `kind` key
//!   and no `context`.
//!
//! Anything else is unroutable and the connection is closed.

use serde_json::Value;

/// The protocol a connection's first frame selects.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Route {
    /// Legacy V4/V5 (`{"context":...}`).
    Legacy,
    /// V6 clean-slate (`{"kind":...}`).
    V6,
    /// Neither shape - reject and close.
    Unknown,
}

/// Route a connection by its first complete frame.
pub fn detect(first_frame: &str) -> Route {
    // Use lenient parsing so a legacy iOS first frame with its `\'`/bare-data
    // quirks still routes to the legacy path (where those quirks belong).
    let Some(value) = mbrc_wire::parse_lenient(first_frame) else {
        return Route::Unknown;
    };
    let Some(obj) = value.as_object() else {
        return Route::Unknown;
    };
    // A string `context` is the unambiguous legacy marker; check it first so a
    // malformed frame carrying both keys still prefers the frozen legacy path.
    if obj.get("context").and_then(Value::as_str).is_some() {
        return Route::Legacy;
    }
    if obj.get("kind").and_then(Value::as_str).is_some() {
        return Route::V6;
    }
    Route::Unknown
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn legacy_first_frames_route_to_legacy() {
        assert_eq!(
            detect(r#"{"context":"player","data":"Android"}"#),
            Route::Legacy
        );
        assert_eq!(detect(r#"{"context":"protocol","data":4}"#), Route::Legacy);
        // The iOS bare-data quirk still routes legacy (lenient parse recovers it).
        assert_eq!(
            detect(r#"{"context":"nowplayingposition","data":status}"#),
            Route::Legacy
        );
    }

    #[test]
    fn v6_handshake_routes_to_v6() {
        assert_eq!(
            detect(
                r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"u","client_type":"cli"}}"#
            ),
            Route::V6
        );
    }

    #[test]
    fn garbage_and_ambiguous_frames_are_unknown() {
        assert_eq!(detect("not json"), Route::Unknown);
        assert_eq!(detect("[1,2,3]"), Route::Unknown);
        assert_eq!(detect(r#"{"data":1}"#), Route::Unknown);
    }
}
