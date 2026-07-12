//! UDP multicast discovery responder. Clients probe the group and we answer
//! with a `notify` frame carrying this host's address, machine name, and the
//! command port, so they can connect without manual configuration.
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

use serde_json::{json, Value};
use socket2::{Domain, Protocol, Socket, Type};
use tokio::net::UdpSocket;
use tokio::sync::Notify;

const MULTICAST_ADDR: Ipv4Addr = Ipv4Addr::new(239, 1, 5, 10);
const DISCOVERY_PORT: u16 = 45345;

/// Answer discovery probes until `shutdown` is signalled.
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
fn hostname() -> String {
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

    // Join the multicast group on every usable interface, not just whichever
    // one the OS picks for INADDR_ANY (on this kind of host that is often a
    // virtual adapter, so the probe arriving on Wi-Fi would never be heard).
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
    // The probe carries the client's own address; prefer it, but fall back to
    // the UDP source IP if the payload is missing/garbled (older or lenient
    // clients), so a malformed probe still gets a usable reply.
    let client_ip = client_address(req).or_else(|| match src.ip() {
        std::net::IpAddr::V4(ip) => Some(ip),
        _ => None,
    });

    let address = advertise_ip(client_ip)
        .map(|ip| ip.to_string())
        .unwrap_or_default();

    if address.is_empty() {
        tracing::debug!(?client_ip, "discovery: no reachable interface to advertise");
    }
    let reply = json!({ "context": "notify", "address": address, "name": name, "port": tcp_port })
        .to_string();
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

/// Parse the client's advertised IPv4 from a probe payload (`{"address": ...}`).
fn client_address(req: &[u8]) -> Option<Ipv4Addr> {
    let value: Value = serde_json::from_slice(req).ok()?;
    value.get("address")?.as_str()?.trim().parse().ok()
}

/// Choose which of this host's addresses to advertise. Mirrors the shipped C#
/// plugin: return the interface on the same subnet as the client, so a
/// multi-NIC host hands back the address the client can actually reach. Falls
/// back to a best-guess private address when there is no client hint or no
/// subnet match.
fn advertise_ip(client_ip: Option<Ipv4Addr>) -> Option<Ipv4Addr> {
    let ifaces = usable_ipv4_ifaces();
    if let Some(client) = client_ip {
        if let Some((ip, _)) = ifaces
            .iter()
            .find(|(ip, mask)| same_subnet(*ip, client, *mask))
        {
            return Some(*ip);
        }
    }
    best_private_ipv4(&ifaces)
}

/// True when `a` and `b` share the network defined by `mask`.
fn same_subnet(a: Ipv4Addr, b: Ipv4Addr, mask: Ipv4Addr) -> bool {
    let (a, b, m) = (u32::from(a), u32::from(b), u32::from(mask));
    a & m == b & m
}

/// Enumerate advertisable IPv4 interfaces as `(ip, netmask)`, dropping the ones
/// a client can never reach: loopback, unspecified, and `169.254.x`
/// link-local (APIPA). Virtual-adapter subnets are left in - the subnet match
/// against the client sorts those out; they only surface as a fallback.
fn usable_ipv4_ifaces() -> Vec<(Ipv4Addr, Ipv4Addr)> {
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
    fn client_address_parsed_from_probe() {
        let req = br#"{"address":"192.168.188.20","port":45345}"#;
        assert_eq!(client_address(req), Some("192.168.188.20".parse().unwrap()));
        assert_eq!(client_address(b"not json"), None);
        assert_eq!(client_address(br#"{"nope":1}"#), None);
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
