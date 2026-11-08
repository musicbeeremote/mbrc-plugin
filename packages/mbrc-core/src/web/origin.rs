//! The `Host` allowlist and the `Origin` check.
//!
//! Both apply whatever `web_auth_required` says. The `Host` allowlist is a
//! DNS-rebinding defence: an attacker who cannot pair can still point a name they
//! control at this machine's address and have the victim's own browser make
//! same-origin requests here. The `Origin` check covers what the allowlist cannot:
//! a page on any site aiming at this machine's IP literal, which is a valid `Host`.
//! A WebSocket upgrade is not subject to CORS, so nothing else stops that page.
//!
//! A request with no `Origin` passes: browsers send it on every cross-origin
//! request that could act, and a native client has no origin to send.

use std::net::IpAddr;

use axum::extract::Request;
use axum::http::{StatusCode, header};
use axum::middleware::Next;
use axum::response::{IntoResponse, Response};

pub async fn guard(request: Request, next: Next) -> Response {
    let headers = request.headers();
    let host = headers
        .get(header::HOST)
        .and_then(|value| value.to_str().ok())
        .unwrap_or_default();

    if !is_allowed(host) {
        tracing::debug!(host, "rejecting request: Host is not a LAN name or address");
        return (StatusCode::MISDIRECTED_REQUEST, "unexpected Host").into_response();
    }
    if let Some(origin) = headers.get(header::ORIGIN)
        && !origin
            .to_str()
            .is_ok_and(|origin| same_origin(origin, host))
    {
        tracing::debug!(?origin, host, "rejecting request: Origin is another site");
        return (StatusCode::FORBIDDEN, "cross-origin request refused").into_response();
    }
    next.run(request).await
}

/// Whether an `Origin` header names the host and port this request was sent to.
///
/// The scheme is not compared, so a TLS-terminating proxy in front of the plugin
/// keeps working. An opaque origin (`null`, from a sandboxed frame or a `file:`
/// page) names no host and never matches.
fn same_origin(origin: &str, host: &str) -> bool {
    let Some((scheme, authority)) = origin.split_once("://") else {
        return false;
    };
    let default_port = match scheme.to_ascii_lowercase().as_str() {
        "http" => ":80",
        "https" => ":443",
        _ => return false,
    };
    let normalized = |value: &str| {
        let value = value.to_ascii_lowercase();
        match value.strip_suffix(default_port) {
            Some(name) => name.to_owned(),
            None => value,
        }
    };
    normalized(authority) == normalized(host)
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

    #[test]
    fn a_page_served_from_this_host_and_port_is_same_origin() {
        assert!(same_origin("http://192.168.1.20:3000", "192.168.1.20:3000"));
        assert!(same_origin("http://THOTH:3000", "thoth:3000"));
        assert!(same_origin("http://[::1]:3000", "[::1]:3000"));
        assert!(same_origin("https://desk.local", "desk.local"));
    }

    #[test]
    fn a_default_port_means_the_same_with_or_without_it() {
        assert!(same_origin("http://thoth", "thoth:80"));
        assert!(same_origin("http://thoth:80", "thoth"));
        assert!(same_origin("https://thoth", "thoth:443"));
        assert!(!same_origin("https://thoth", "thoth:80"));
    }

    #[test]
    fn another_site_or_port_is_not_same_origin() {
        assert!(!same_origin("https://evil.example", "192.168.1.20:3000"));
        assert!(!same_origin(
            "http://192.168.1.20:8080",
            "192.168.1.20:3000"
        ));
        assert!(!same_origin(
            "http://192.168.1.21:3000",
            "192.168.1.20:3000"
        ));
        assert!(!same_origin("http://192.168.1.20", "192.168.1.20:3000"));
    }

    #[test]
    fn an_opaque_or_foreign_scheme_origin_is_refused() {
        assert!(!same_origin("null", "192.168.1.20:3000"));
        assert!(!same_origin("", "192.168.1.20:3000"));
        assert!(!same_origin("file://", "192.168.1.20:3000"));
        assert!(!same_origin("ftp://thoth:3000", "thoth:3000"));
    }

    proptest::proptest! {
        /// The Host header is the first untrusted input every request meets.
        #[test]
        fn any_host_header_is_judged_without_panicking(host in "(?s).{0,80}") {
            let _ = is_allowed(&host);
            let _ = strip_port(&host);
        }

        #[test]
        fn any_origin_header_is_judged_without_panicking(
            origin in "(?s).{0,80}", host in "(?s).{0,40}",
        ) {
            let _ = same_origin(&origin, &host);
        }

        #[test]
        fn a_bracketed_host_with_any_tail_is_judged_without_panicking(
            inner in "[0-9a-fA-F:.%]{0,40}", tail in "[\\]:0-9a-z]{0,12}",
        ) {
            let _ = is_allowed(&format!("[{inner}{tail}"));
        }
    }
}
