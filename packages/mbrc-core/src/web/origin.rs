//! The `Host` allowlist.
//!
//! A DNS-rebinding defence, and the reason it applies whatever
//! `web_auth_required` says: an attacker who cannot pair can still point a name
//! they control at this machine's address and have the victim's own browser make
//! same-origin requests here. Pinning `Host` to the shapes a LAN client actually
//! uses removes that, and costs a user nothing.

use std::net::IpAddr;

use axum::extract::Request;
use axum::http::{StatusCode, header};
use axum::middleware::Next;
use axum::response::{IntoResponse, Response};

pub async fn guard(request: Request, next: Next) -> Response {
    let host = request
        .headers()
        .get(header::HOST)
        .and_then(|value| value.to_str().ok())
        .unwrap_or_default();

    if !is_allowed(host) {
        tracing::debug!(host, "rejecting request: Host is not a LAN name or address");
        return (StatusCode::MISDIRECTED_REQUEST, "unexpected Host").into_response();
    }
    next.run(request).await
}

/// Whether a `Host` header names this server the way a LAN client reaches it.
///
/// An address, or a name that cannot exist in the public DNS and so cannot be
/// aimed at this machine by anyone not already on the network: a single label
/// (`thoth`, how Windows and a home router name a machine, and which the root
/// is forbidden to delegate), the `.local` mDNS advertises, or a suffix
/// reserved for private use. A suffix a router invents under a real gTLD is
/// not one of those.
fn is_allowed(host: &str) -> bool {
    let name = strip_port(host);
    if name.is_empty() {
        return false;
    }
    if name.parse::<IpAddr>().is_ok() {
        return true;
    }
    let name = name.trim_end_matches('.').to_ascii_lowercase();
    !name.contains('.')
        || name.ends_with(".local")
        || name.ends_with(".home.arpa")
        || name.ends_with(".internal")
}

/// Splits the port off a `Host`, handling the bracketed IPv6 literal form.
fn strip_port(host: &str) -> &str {
    if let Some(end) = host.strip_prefix('[').and_then(|rest| rest.find(']')) {
        return &host[1..=end];
    }
    host.rsplit_once(':').map_or(host, |(name, _)| name)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn addresses_localhost_and_mdns_names_are_allowed() {
        assert!(is_allowed("192.168.1.20:3000"));
        assert!(is_allowed("10.0.0.5"));
        assert!(is_allowed("localhost:3000"));
        assert!(is_allowed("LOCALHOST"));
        assert!(is_allowed("desktop.local:3000"));
        assert!(is_allowed("[::1]:3000"));
        assert!(is_allowed("[fe80::1]"));
    }

    /// The name a phone is handed for a Windows machine on a home network has no
    /// suffix at all, and the root cannot delegate a dotless name, so it is not
    /// one an attacker can point anywhere.
    #[test]
    fn a_bare_machine_name_is_allowed() {
        assert!(is_allowed("thoth:3000"));
        assert!(is_allowed("THOTH"));
        assert!(is_allowed("thoth.:3000"), "the fully qualified root dot");
        assert!(is_allowed("desk.home.arpa:3000"));
        assert!(is_allowed("desk.internal"));
    }

    #[test]
    fn a_rebinding_name_is_rejected() {
        assert!(!is_allowed("evil.example:3000"));
        assert!(!is_allowed("musicbee.attacker.com"));
        assert!(!is_allowed(""));
        assert!(!is_allowed("thoth.fritz.box:3000"), ".box is a real gTLD");
        assert!(!is_allowed("thoth.lan"));
    }
}
