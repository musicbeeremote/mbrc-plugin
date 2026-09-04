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

/// Binds a multicast socket to `iface`, joins the group on it, and sends the
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

/// The DNS-SD service type the plugin advertises (#160), alongside the custom
/// protocol above.
///
/// Must match `mbrc_core::mdns::SERVICE_TYPE`; the two crates do not share a
/// dependency, the same way the multicast constants above are mirrored rather
/// than shared.
pub const MDNS_SERVICE_TYPE: &str = "_mbrc._tcp.local.";

/// Blocking mDNS browse: collect the plugin instances that answer a DNS-SD query
/// until `timeout` elapses.
///
/// The other half of [`discover_blocking`], answering "is this host
/// advertising, and with what?" without a packet capture. mDNS carries no
/// subnet hint, so unlike the custom probe this returns whatever the record
/// lists, and a multi-NIC host can yield several entries.
///
/// # Errors
/// The mDNS daemon could not start, or the browse could not be registered.
pub fn browse_mdns_blocking(timeout: Duration) -> Result<Vec<Discovered>, String> {
    let daemon = mdns_sd::ServiceDaemon::new().map_err(|e| format!("mDNS daemon: {e}"))?;
    let receiver = daemon
        .browse(MDNS_SERVICE_TYPE)
        .map_err(|e| format!("mDNS browse: {e}"))?;

    let mut found: Vec<Discovered> = Vec::new();
    let deadline = Instant::now() + timeout;
    while let Some(remaining) = deadline.checked_duration_since(Instant::now()) {
        match receiver.recv_timeout(remaining) {
            Ok(mdns_sd::ServiceEvent::ServiceResolved(info)) => {
                let name = info
                    .get_property_val_str("name")
                    .map(str::to_owned)
                    .unwrap_or_else(|| instance_of(info.get_fullname()));
                for address in info.get_addresses() {
                    let entry = Discovered {
                        address: address.to_string(),
                        port: info.get_port(),
                        name: name.clone(),
                    };
                    if !found
                        .iter()
                        .any(|d| d.address == entry.address && d.port == entry.port)
                    {
                        found.push(entry);
                    }
                }
            }
            Ok(_) => {}
            Err(_) => break,
        }
    }
    let _ = daemon.shutdown();
    Ok(found)
}

/// The instance label out of a DNS-SD full name (`INSTANCE._mbrc._tcp.local.`),
/// for when the record carries no `name` in TXT.
fn instance_of(fullname: &str) -> String {
    fullname
        .strip_suffix(MDNS_SERVICE_TYPE)
        .and_then(|s| s.strip_suffix('.'))
        .unwrap_or(fullname)
        .to_owned()
}

/// Blocking discovery: probe every interface and collect distinct replies until
/// `timeout` elapses. Runs on the calling thread.
///
/// # Errors
/// No interface accepted a discovery socket, or a receive failed.
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

/// Parses a `notify` reply into a discovered instance, if it is one.
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
    fn instance_label_is_taken_from_the_full_name() {
        assert_eq!(instance_of("THOTH._mbrc._tcp.local."), "THOTH");
        // Anything that is not the expected shape is passed through rather than
        // mangled: a label is better than an empty name.
        assert_eq!(instance_of("odd-name"), "odd-name");
    }

    #[test]
    fn ignores_non_notify_and_junk() {
        assert!(parse_notify(br#"{"context":"discovery","address":"x"}"#).is_none());
        assert!(parse_notify(b"not json").is_none());
        assert!(parse_notify(br#"{"context":"notify"}"#).is_none()); // no address
    }
}
