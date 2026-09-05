//! UDP multicast discovery responder.
//!
//! Clients probe the group and we answer with a `notify` frame carrying this
//! host's address, machine name, and the command port, so they can connect
//! without manual configuration.
//!
//! Robustness matters here: a MusicBee host commonly has several NICs (Wi-Fi
//! plus Hyper-V / WSL / VirtualBox / Docker virtual adapters, plus APIPA
//! `169.254.x` link-local). Advertising the wrong one hands the phone an
//! unreachable IP even though "discovery worked". So, matching the shipped C#
//! plugin, we read the client's own address from the probe and reply with the
//! server interface on the *same subnet*; we also join the multicast group on
//! every usable interface so the probe is heard regardless of NIC.
//!
//! Best-effort: if the socket can't bind (port busy, no multicast) the
//! responder logs and exits without affecting the TCP server.

use std::net::{Ipv4Addr, SocketAddr};
use std::sync::Arc;

use serde_json::{Value, json};
use socket2::{Domain, Protocol, Socket, Type};
use tokio::net::UdpSocket;
use tokio::sync::Notify;

const MULTICAST_ADDR: Ipv4Addr = Ipv4Addr::new(239, 1, 5, 10);
const DISCOVERY_PORT: u16 = 45345;

/// Answers discovery probes until `shutdown` is signalled.
pub async fn run(tcp_port: u16, shutdown: Arc<Notify>) {
    let socket = match bind() {
        Ok(socket) => socket,
        Err(e) => {
            tracing::warn!(error = %e, "discovery responder disabled (bind failed)");
            return;
        }
    };
    let name = hostname();
    tracing::info!(port = DISCOVERY_PORT, name = %name, "discovery responder listening");

    let mut buf = [0u8; 1024];
    loop {
        tokio::select! {
            _ = shutdown.notified() => return,
            result = socket.recv_from(&mut buf) => match result {
                Ok((len, src)) => respond(&socket, src, &buf[..len], tcp_port, &name).await,
                Err(e) => tracing::debug!(error = %e, "discovery recv error"),
            }
        }
    }
}

/// The device name advertised to clients - this host's machine name. On Windows
/// `COMPUTERNAME` is the equivalent of the shipped plugin's
/// `Environment.MachineName`; fall back to a generic label if unset.
///
/// Shared with the mDNS advertisement, so both mechanisms name this host the
/// same way in a picker.
pub(crate) fn hostname() -> String {
    std::env::var("COMPUTERNAME")
        .or_else(|_| std::env::var("HOSTNAME"))
        .ok()
        .filter(|s| !s.trim().is_empty())
        .unwrap_or_else(|| "MusicBee".to_string())
}

fn bind() -> std::io::Result<UdpSocket> {
    let socket = Socket::new(Domain::IPV4, Type::DGRAM, Some(Protocol::UDP))?;
    socket.set_reuse_address(true)?;
    let addr: SocketAddr = (Ipv4Addr::UNSPECIFIED, DISCOVERY_PORT).into();
    socket.bind(&addr.into())?;

    // Every usable interface, not just the one INADDR_ANY picks: on this kind
    // of host that is often a virtual adapter.
    let mut joined = 0usize;
    for (ip, _netmask) in usable_ipv4_ifaces() {
        match socket.join_multicast_v4(&MULTICAST_ADDR, &ip) {
            Ok(()) => {
                joined += 1;
                tracing::debug!(interface = %ip, "joined discovery multicast group");
            }
            Err(e) => tracing::debug!(interface = %ip, error = %e, "multicast join failed"),
        }
    }
    if joined == 0 {
        // Nothing usable enumerated (or every join failed): fall back to letting
        // the OS choose, so discovery still has a chance of working.
        socket.join_multicast_v4(&MULTICAST_ADDR, &Ipv4Addr::UNSPECIFIED)?;
        tracing::debug!("no per-interface multicast join; fell back to INADDR_ANY");
    }

    socket.set_nonblocking(true)?;
    UdpSocket::from_std(socket.into())
}

async fn respond(socket: &UdpSocket, src: SocketAddr, req: &[u8], tcp_port: u16, name: &str) {
    let probe = Probe::parse(req);
    // Prefer the address the probe carries, falling back to the UDP source so
    // a malformed probe still gets a usable reply.
    let client_ip = probe.client_ip.or_else(|| match src.ip() {
        std::net::IpAddr::V4(ip) => Some(ip),
        _ => None,
    });

    let address = advertise_ip(client_ip)
        .map(|ip| ip.to_string())
        .unwrap_or_default();

    if address.is_empty() {
        tracing::debug!(?client_ip, "discovery: no reachable interface to advertise");
    }
    let reply = notify(&address, name, tcp_port, probe.wants_protocols).to_string();
    match socket.send_to(reply.as_bytes(), src).await {
        Ok(_) => tracing::debug!(
            %src,
            ?client_ip,
            advertised = %address,
            port = tcp_port,
            "discovery probe answered"
        ),
        Err(e) => tracing::debug!(%src, error = %e, "discovery reply failed"),
    }
}

/// What a probe asked for.
///
/// Shipped clients send `{"address": "..."}` and read four fixed keys out of the
/// reply, so nothing new may appear in it unasked. A client that also wants to
/// know which protocols the port serves sets `"protocol": true`, and only that
/// probe is answered with the list.
struct Probe {
    client_ip: Option<Ipv4Addr>,
    wants_protocols: bool,
}

impl Probe {
    fn parse(req: &[u8]) -> Self {
        let Ok(value) = serde_json::from_slice::<Value>(req) else {
            return Self {
                client_ip: None,
                wants_protocols: false,
            };
        };
        Self {
            client_ip: value
                .get("address")
                .and_then(Value::as_str)
                .and_then(|a| a.trim().parse().ok()),
            wants_protocols: value.get("protocol").and_then(Value::as_bool) == Some(true),
        }
    }
}

/// The reply frame. The four original keys keep their order and their spelling;
/// `protocol` is appended only for a probe that asked, so a shipped client's
/// reply stays byte-identical to what the C# plugin sent.
fn notify(address: &str, name: &str, tcp_port: u16, with_protocols: bool) -> Value {
    let mut reply =
        json!({ "context": "notify", "address": address, "name": name, "port": tcp_port });
    if with_protocols && let Some(obj) = reply.as_object_mut() {
        obj.insert(
            "protocol".to_owned(),
            json!(crate::protocol::version::advertised_protocols_csv()),
        );
    }
    reply
}

/// Chooses which of this host's addresses to advertise. Mirrors the shipped C#
/// plugin: return the interface on the same subnet as the client, so a
/// multi-NIC host hands back the address the client can actually reach. Falls
/// back to a best-guess private address when there is no client hint or no
/// subnet match.
fn advertise_ip(client_ip: Option<Ipv4Addr>) -> Option<Ipv4Addr> {
    let ifaces = usable_ipv4_ifaces();
    if let Some(client) = client_ip
        && let Some((ip, _)) = ifaces
            .iter()
            .find(|(ip, mask)| same_subnet(*ip, client, *mask))
    {
        return Some(*ip);
    }
    best_private_ipv4(&ifaces)
}

/// True when `a` and `b` share the network defined by `mask`.
fn same_subnet(a: Ipv4Addr, b: Ipv4Addr, mask: Ipv4Addr) -> bool {
    let (a, b, m) = (u32::from(a), u32::from(b), u32::from(mask));
    a & m == b & m
}

/// Enumerates advertisable IPv4 interfaces as `(ip, netmask)`, dropping the ones
/// a client can never reach: loopback, unspecified, and `169.254.x`
/// link-local (APIPA). Virtual-adapter subnets are left in - the subnet match
/// against the client sorts those out; they only surface as a fallback.
///
/// Also the source for the settings panel's "reachable at" list (via
/// `HostQueryType::ListeningAddresses`): the same set the discovery responder
/// would advertise is exactly what the user should point a client at.
pub(crate) fn usable_ipv4_ifaces() -> Vec<(Ipv4Addr, Ipv4Addr)> {
    if_addrs::get_if_addrs()
        .unwrap_or_default()
        .into_iter()
        .filter_map(|iface| match iface.addr {
            if_addrs::IfAddr::V4(v4)
                if !v4.ip.is_loopback() && !v4.ip.is_unspecified() && !v4.ip.is_link_local() =>
            {
                Some((v4.ip, v4.netmask))
            }
            _ => None,
        })
        .collect()
}

/// Fallback pick when the client subnet can't be matched: prefer a genuine
/// private LAN address (`192.168/16`, `10/8`, `172.16/12`) over anything else.
fn best_private_ipv4(ifaces: &[(Ipv4Addr, Ipv4Addr)]) -> Option<Ipv4Addr> {
    ifaces
        .iter()
        .find(|(ip, _)| ip.is_private())
        .or_else(|| ifaces.first())
        .map(|(ip, _)| *ip)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// The reply a shipped client gets must stay exactly what the C# plugin
    /// sent - same keys, same order, nothing extra - because those clients are
    /// frozen and this payload has never changed under them.
    #[test]
    fn a_plain_probe_is_answered_with_the_four_original_keys() {
        let probe = Probe::parse(br#"{"address":"192.168.1.5"}"#);
        assert!(!probe.wants_protocols);

        let reply = notify("192.168.1.10", "LIVING-ROOM", 3000, probe.wants_protocols);
        assert_eq!(
            reply.to_string(),
            r#"{"context":"notify","address":"192.168.1.10","name":"LIVING-ROOM","port":3000}"#
        );
    }

    /// The opt-in: a client that says it can read the list gets it, appended
    /// after the original keys so the prefix a shipped client parses is
    /// unchanged even if it ever sees this reply.
    #[test]
    fn a_probe_that_asks_is_told_which_protocols_the_port_serves() {
        let probe = Probe::parse(br#"{"address":"192.168.1.5","protocol":true}"#);
        assert!(probe.wants_protocols);

        let reply = notify("192.168.1.10", "LIVING-ROOM", 3000, probe.wants_protocols);
        assert_eq!(
            reply["protocol"],
            json!(crate::protocol::version::advertised_protocols_csv())
        );
        assert!(reply.to_string().starts_with(
            r#"{"context":"notify","address":"192.168.1.10","name":"LIVING-ROOM","port":3000,"#
        ));
    }

    /// Anything but `true` is not an opt-in: a client that sends the key with a
    /// version number or a string has not told us it can parse the list.
    #[test]
    fn only_a_true_flag_opts_in() {
        for req in [
            br#"{"protocol":6}"#.as_slice(),
            br#"{"protocol":"6"}"#.as_slice(),
            br#"{"protocol":false}"#.as_slice(),
            b"not json at all",
        ] {
            assert!(!Probe::parse(req).wants_protocols, "{req:?}");
        }
    }

    #[test]
    fn a_probe_address_is_read_and_a_malformed_one_is_not_fatal() {
        assert_eq!(
            Probe::parse(br#"{"address":"192.168.188.20","port":45345}"#).client_ip,
            Some("192.168.188.20".parse().unwrap())
        );
        assert_eq!(
            Probe::parse(br#"{"address":" 192.168.1.5 "}"#).client_ip,
            Some("192.168.1.5".parse().unwrap())
        );
        assert_eq!(Probe::parse(b"{}").client_ip, None);
        assert_eq!(Probe::parse(b"garbage").client_ip, None);
    }

    #[test]
    fn subnet_match_picks_the_reachable_interface() {
        // Wi-Fi on the client's /24, plus a Hyper-V and a link-local-style NIC.
        let ifaces = [
            (
                "172.24.160.1".parse().unwrap(),
                "255.255.240.0".parse().unwrap(),
            ),
            (
                "192.168.188.37".parse().unwrap(),
                "255.255.255.0".parse().unwrap(),
            ),
            ("10.0.0.5".parse().unwrap(), "255.0.0.0".parse().unwrap()),
        ];
        let client: Ipv4Addr = "192.168.188.20".parse().unwrap();
        let picked = ifaces
            .iter()
            .find(|(ip, mask)| same_subnet(*ip, client, *mask))
            .map(|(ip, _)| *ip);
        assert_eq!(picked, Some("192.168.188.37".parse().unwrap()));
    }

    #[test]
    fn same_subnet_respects_mask_width() {
        let mask: Ipv4Addr = "255.255.255.0".parse().unwrap();
        assert!(same_subnet(
            "192.168.1.10".parse().unwrap(),
            "192.168.1.200".parse().unwrap(),
            mask
        ));
        assert!(!same_subnet(
            "192.168.1.10".parse().unwrap(),
            "192.168.2.10".parse().unwrap(),
            mask
        ));
    }
}
