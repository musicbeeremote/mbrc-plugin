//! Service discovery - find MusicBee Remote plugin instances on the LAN.
//!
//! Mirrors the plugin's UDP-multicast discovery: multicast a `discovery`
//! request to `239.1.5.10:45345`, then collect `notify` replies
//! (`{"context":"notify","address","name","port"}`) until a short timeout.
//! Pure `std::net` on the calling thread - callers that need async wrap
//! [`discover_blocking`] in their own runtime's blocking pool.
//!
//! The request is sent from every non-loopback IPv4 interface (enumerated via
//! `if-addrs`), not just the default route. A machine with multiple NICs (Wi-Fi
//! and Ethernet, a VPN, Hyper-V/WSL bridges) would otherwise miss plugins
//! reachable only on a non-default interface - the original C# tool enumerated
//! interfaces for the same reason.

use std::net::{Ipv4Addr, SocketAddrV4, UdpSocket};
use std::time::{Duration, Instant};

use serde::Serialize;

const MULTICAST: Ipv4Addr = Ipv4Addr::new(239, 1, 5, 10);
const DISCOVERY_PORT: u16 = 45345;
const DEFAULT_PLUGIN_PORT: u16 = 3000;

#[derive(Debug, Clone, Serialize)]
pub struct Discovered {
    pub address: String,
    pub port: u16,
    pub name: String,
}

/// Non-loopback IPv4 addresses of the local interfaces. One multicast socket is
/// bound per address so the request goes out every NIC. Falls back to
/// `UNSPECIFIED` (default-route only) if enumeration fails or finds nothing.
fn interface_ipv4s() -> Vec<Ipv4Addr> {
    let mut addrs: Vec<Ipv4Addr> = if_addrs::get_if_addrs()
        .map(|ifaces| {
            ifaces
                .into_iter()
                .filter_map(|i| match i.addr {
                    if_addrs::IfAddr::V4(v4) if !v4.ip.is_loopback() => Some(v4.ip),
                    _ => None,
                })
                .collect()
        })
        .unwrap_or_default();
    addrs.sort();
    addrs.dedup();
    if addrs.is_empty() {
        addrs.push(Ipv4Addr::UNSPECIFIED);
    }
    addrs
}

/// Bind a multicast socket to `iface`, join the group on it, and send the
/// discovery request advertising `iface` as the reply-to address.
fn open_and_send(iface: Ipv4Addr) -> std::io::Result<UdpSocket> {
    // Binding to the interface's own IP makes the OS route outgoing multicast
    // out that interface (std has no `set_multicast_if_v4`).
    let socket = UdpSocket::bind((iface, 0))?;
    socket.join_multicast_v4(&MULTICAST, &iface)?;
    // Poll in short slices so the overall timeout is honored without blocking long.
    socket.set_read_timeout(Some(Duration::from_millis(250)))?;
    let request = format!(r#"{{"context":"discovery","address":"{iface}"}}"#);
    socket.send_to(
        request.as_bytes(),
        SocketAddrV4::new(MULTICAST, DISCOVERY_PORT),
    )?;
    Ok(socket)
}

/// Blocking discovery: probe every interface and collect distinct replies until
/// `timeout` elapses. Runs on the calling thread.
pub fn discover_blocking(timeout: Duration) -> Result<Vec<Discovered>, String> {
    let ifaces = interface_ipv4s();
    // Best-effort per interface: a NIC that can't join the group (e.g. a
    // point-to-point VPN) shouldn't abort discovery on the others.
    let sockets: Vec<(Ipv4Addr, UdpSocket)> = ifaces
        .iter()
        .filter_map(|&ip| open_and_send(ip).ok().map(|s| (ip, s)))
        .collect();
    if sockets.is_empty() {
        return Err("could not open a discovery socket on any interface".into());
    }

    let mut found: Vec<Discovered> = Vec::new();
    let deadline = Instant::now() + timeout;
    let mut buf = [0u8; 4096];
    while Instant::now() < deadline {
        for (_ip, socket) in &sockets {
            match socket.recv_from(&mut buf) {
                Ok((n, _src)) => {
                    if let Some(d) = parse_notify(&buf[..n]) {
                        if !found
                            .iter()
                            .any(|e| e.address == d.address && e.port == d.port)
                        {
                            found.push(d);
                        }
                    }
                }
                Err(ref e)
                    if e.kind() == std::io::ErrorKind::WouldBlock
                        || e.kind() == std::io::ErrorKind::TimedOut => {}
                Err(e) => return Err(format!("recv failed: {e}")),
            }
        }
    }
    for (ip, socket) in &sockets {
        let _ = socket.leave_multicast_v4(&MULTICAST, ip);
    }
    Ok(found)
}

/// Parse a `notify` reply into a discovered instance, if it is one.
fn parse_notify(bytes: &[u8]) -> Option<Discovered> {
    let v: serde_json::Value = serde_json::from_slice(bytes).ok()?;
    if v.get("context")?.as_str()? != "notify" {
        return None;
    }
    let address = v.get("address")?.as_str()?.to_string();
    if address.is_empty() {
        return None;
    }
    let port = v
        .get("port")
        .and_then(|p| p.as_u64())
        .unwrap_or(DEFAULT_PLUGIN_PORT as u64) as u16;
    let name = v
        .get("name")
        .and_then(|n| n.as_str())
        .filter(|s| !s.is_empty())
        .unwrap_or("MusicBee Remote")
        .to_string();
    Some(Discovered {
        address,
        port,
        name,
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_a_notify_reply() {
        let d = parse_notify(
            br#"{"context":"notify","address":"192.168.1.5","name":"Den PC","port":3000}"#,
        )
        .expect("should parse");
        assert_eq!(d.address, "192.168.1.5");
        assert_eq!(d.port, 3000);
        assert_eq!(d.name, "Den PC");
    }

    #[test]
    fn defaults_port_and_name_when_absent() {
        let d =
            parse_notify(br#"{"context":"notify","address":"10.0.0.2"}"#).expect("should parse");
        assert_eq!(d.port, 3000);
        assert_eq!(d.name, "MusicBee Remote");
    }

    #[test]
    fn ignores_non_notify_and_junk() {
        assert!(parse_notify(br#"{"context":"discovery","address":"x"}"#).is_none());
        assert!(parse_notify(b"not json").is_none());
        assert!(parse_notify(br#"{"context":"notify"}"#).is_none()); // no address
    }
}
