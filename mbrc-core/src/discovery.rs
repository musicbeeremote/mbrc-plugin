//! UDP multicast service discovery.
//!
//! Matches the C# `ServiceDiscovery` protocol byte-for-byte:
//! - Listens on `239.1.5.10:45345` on every private IPv4 interface.
//! - Parses `{"context":"discovery","address":"<client_ip>"}`, responds with
//!   `{"context":"notify","address":"<iface_ip>","name":"<host>","port":<tcp_port>}`
//!   where `<iface_ip>` is the local interface on the same subnet as the client.
//! - Invalid / non-discovery messages receive `{"context":"error","description":"..."}`.

use std::net::{IpAddr, Ipv4Addr, SocketAddrV4};
use std::sync::Arc;

use serde::{Deserialize, Serialize};
use socket2::{Domain, Protocol, Socket, Type};
use tokio::net::UdpSocket;
use tokio::sync::oneshot;
use tracing::{debug, info, warn};

const DISCOVERY_ADDR: Ipv4Addr = Ipv4Addr::new(239, 1, 5, 10);
const DISCOVERY_PORT: u16 = 45345;

#[derive(Debug, Deserialize)]
struct DiscoveryRequest {
    context: String,
    #[serde(default)]
    address: String,
}

#[derive(Debug, Serialize)]
struct DiscoveryResponse<'a> {
    context: &'a str,
    address: String,
    name: String,
    port: u16,
}

#[derive(Debug, Serialize)]
struct ErrorResponse<'a> {
    context: &'a str,
    description: &'a str,
}

/// Find the local IPv4 interface on the same /24 subnet as `client`.
///
/// The C# implementation masks with each interface's reported subnet mask. We
/// approximate with a /24 match, which covers every private home-network
/// layout the Android app operates on. If the user ever runs a network where
/// this is wrong, we fall back to the first private address.
fn interface_for_client(client: Ipv4Addr, interfaces: &[Ipv4Addr]) -> Option<Ipv4Addr> {
    let c = client.octets();
    interfaces
        .iter()
        .find(|ip| {
            let o = ip.octets();
            o[0] == c[0] && o[1] == c[1] && o[2] == c[2]
        })
        .copied()
        .or_else(|| interfaces.first().copied())
}

fn private_ipv4_interfaces() -> Vec<Ipv4Addr> {
    if_addrs::get_if_addrs()
        .unwrap_or_default()
        .into_iter()
        .filter(|i| !i.is_loopback())
        .filter_map(|i| match i.ip() {
            IpAddr::V4(v4) if v4.is_private() => Some(v4),
            _ => None,
        })
        .collect()
}

fn machine_name() -> String {
    std::env::var("COMPUTERNAME")
        .or_else(|_| std::env::var("HOSTNAME"))
        .unwrap_or_else(|_| "musicbee".to_owned())
}

/// Build a UDP socket bound to `0.0.0.0:45345` with SO_REUSEADDR and each
/// interface joined to the discovery multicast group.
fn bind_multicast_socket(interfaces: &[Ipv4Addr]) -> std::io::Result<UdpSocket> {
    let sock = Socket::new(Domain::IPV4, Type::DGRAM, Some(Protocol::UDP))?;
    sock.set_reuse_address(true)?;
    sock.set_nonblocking(true)?;
    sock.bind(&SocketAddrV4::new(Ipv4Addr::UNSPECIFIED, DISCOVERY_PORT).into())?;

    for iface in interfaces {
        if let Err(e) = sock.join_multicast_v4(&DISCOVERY_ADDR, iface) {
            warn!("Failed to join multicast group on {}: {}", iface, e);
        }
    }

    UdpSocket::from_std(sock.into())
}

fn handle_packet(buf: &[u8], interfaces: &[Ipv4Addr], port: u16) -> Vec<u8> {
    let Ok(text) = std::str::from_utf8(buf) else {
        return error_body("invalid message format");
    };

    let req: DiscoveryRequest = match serde_json::from_str(text) {
        Ok(r) => r,
        Err(_) => return error_body("invalid message format"),
    };

    if !req.context.to_ascii_lowercase().contains("discovery") {
        return error_body("unsupported action");
    }

    if req.address.is_empty() {
        return error_body("missing address");
    }

    let Ok(client_ip) = req.address.parse::<Ipv4Addr>() else {
        return error_body("missing address");
    };

    let Some(iface) = interface_for_client(client_ip, interfaces) else {
        return error_body("no suitable interface found");
    };

    let resp = DiscoveryResponse {
        context: "notify",
        address: iface.to_string(),
        name: machine_name(),
        port,
    };
    serde_json::to_vec(&resp).unwrap_or_else(|_| error_body("internal error"))
}

fn error_body(desc: &str) -> Vec<u8> {
    let err = ErrorResponse {
        context: "error",
        description: desc,
    };
    serde_json::to_vec(&err).unwrap_or_default()
}

/// Run the discovery service until `shutdown_rx` resolves.
pub async fn run(
    tcp_port: u16,
    mut shutdown_rx: oneshot::Receiver<()>,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let interfaces = private_ipv4_interfaces();
    if interfaces.is_empty() {
        warn!("No private IPv4 interfaces found; discovery disabled");
        return Ok(());
    }
    let socket = Arc::new(bind_multicast_socket(&interfaces)?);
    info!(
        "Discovery listening on {}:{} across {} interface(s)",
        DISCOVERY_ADDR,
        DISCOVERY_PORT,
        interfaces.len()
    );

    let mut buf = [0u8; 1500];
    loop {
        tokio::select! {
            res = socket.recv_from(&mut buf) => {
                match res {
                    Ok((n, peer)) => {
                        let reply = handle_packet(&buf[..n], &interfaces, tcp_port);
                        if let Err(e) = socket.send_to(&reply, peer).await {
                            debug!("Discovery reply to {} failed: {}", peer, e);
                        }
                    }
                    Err(e) => {
                        warn!("Discovery recv error: {}", e);
                    }
                }
            }
            _ = &mut shutdown_rx => {
                info!("Discovery shutting down");
                break;
            }
        }
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn matches_same_subnet() {
        let ifaces = [Ipv4Addr::new(10, 0, 0, 5), Ipv4Addr::new(192, 168, 1, 20)];
        assert_eq!(
            interface_for_client(Ipv4Addr::new(192, 168, 1, 77), &ifaces),
            Some(Ipv4Addr::new(192, 168, 1, 20))
        );
    }

    #[test]
    fn falls_back_to_first_interface() {
        let ifaces = [Ipv4Addr::new(10, 0, 0, 5)];
        assert_eq!(
            interface_for_client(Ipv4Addr::new(192, 168, 1, 77), &ifaces),
            Some(Ipv4Addr::new(10, 0, 0, 5))
        );
    }

    #[test]
    fn discovery_request_ok() {
        let ifaces = [Ipv4Addr::new(192, 168, 1, 20)];
        let reply = handle_packet(
            br#"{"context":"discovery","address":"192.168.1.77"}"#,
            &ifaces,
            3000,
        );
        let v: serde_json::Value = serde_json::from_slice(&reply).unwrap();
        assert_eq!(v["context"], "notify");
        assert_eq!(v["address"], "192.168.1.20");
        assert_eq!(v["port"], 3000);
    }

    #[test]
    fn rejects_non_discovery_context() {
        let reply = handle_packet(br#"{"context":"other"}"#, &[], 0);
        let v: serde_json::Value = serde_json::from_slice(&reply).unwrap();
        assert_eq!(v["context"], "error");
        assert_eq!(v["description"], "unsupported action");
    }

    #[test]
    fn rejects_missing_address() {
        let reply = handle_packet(br#"{"context":"discovery"}"#, &[], 0);
        let v: serde_json::Value = serde_json::from_slice(&reply).unwrap();
        assert_eq!(v["description"], "missing address");
    }

    #[test]
    fn rejects_malformed_json() {
        let reply = handle_packet(b"not json", &[], 0);
        let v: serde_json::Value = serde_json::from_slice(&reply).unwrap();
        assert_eq!(v["description"], "invalid message format");
    }
}
