//! The HTTP and WebSocket transport, served on the command port.
//!
//! A browser is a V6 client that cannot open a raw socket, so this module is a
//! transport and nothing more: every request ends in the same
//! [`commands_v6::dispatch`](crate::server::commands_v6::dispatch) the TCP path
//! calls, and the WebSocket feeds the same
//! [`V6Session`](crate::server::session_v6::V6Session). There is no second
//! command surface to keep in sync, and no V4/V5 here at all - the legacy
//! protocol is frozen on its own transport.

mod assets;
pub mod auth;
mod cover;
mod events;
mod origin;
mod router;
mod rpc;
mod ws;

pub use router::serve;

/// HTTP method prefixes that route a connection to this module.
///
/// Sniffed from the first bytes rather than parsed, because the JSON protocols
/// are routed by parsing their first frame and an HTTP request is not JSON.
const METHOD_PREFIXES: &[&[u8]] = &[
    b"GET ", b"POST", b"HEAD", b"PUT ", b"DELE", b"OPTI", b"PATC",
];

/// Whether a connection's opening bytes are an HTTP request line.
pub fn sniff(head: &[u8]) -> bool {
    METHOD_PREFIXES
        .iter()
        .any(|prefix| head.starts_with(prefix))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn http_request_lines_sniff_as_http() {
        assert!(sniff(b"GET / HTTP/1.1\r\n"));
        assert!(sniff(b"POST /api/v6/player_play HTTP/1.1\r\n"));
        assert!(sniff(b"OPTIONS * HTTP/1.1\r\n"));
    }

    #[test]
    fn json_first_frames_never_sniff_as_http() {
        assert!(!sniff(br#"{"context":"player","data":"Android"}"#));
        assert!(!sniff(
            br#"{"id":0,"kind":"request","op":"handshake","data":{}}"#
        ));
        assert!(!sniff(b""));
        assert!(!sniff(b"not json"));
    }
}
