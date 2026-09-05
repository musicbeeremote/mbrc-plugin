//! mDNS / DNS-SD advertisement, alongside the custom UDP responder (#160).
//!
//! [`discovery`](crate::discovery) stays - every shipped client depends on it -
//! but nothing written against something else can find it, and both mobile
//! platforms browse DNS-SD out of the box. This publishes the same facts through
//! the standard mechanism. Four deliberate choices:
//!
//! - **Best-effort, like the custom responder.** A daemon that will not start
//!   logs a warning and leaves; failing to advertise must never stop the plugin
//!   serving clients.
//! - **A list of services, not one.** One daemon can hold several, so a second
//!   endpoint later is another entry rather than a rewrite.
//! - **The addresses are ours, not the crate's.** We hand `mdns-sd`
//!   `usable_ipv4_ifaces`, so it advertises what the panel's "Reachable at"
//!   row shows. mDNS carries no client-subnet hint, so we cannot pick *the*
//!   reachable address, only avoid publishing loopback and APIPA.
//! - **Goodbye on the way out.** A stale instance lingering in every browser on
//!   the LAN is worse than never having advertised.

use std::collections::HashMap;
use std::net::IpAddr;
use std::sync::Arc;
use std::time::Duration;

use mdns_sd::{ServiceDaemon, ServiceInfo};
use tokio::sync::Notify;

use crate::discovery::usable_ipv4_ifaces;
use crate::protocol::version::ADVERTISED_PROTOCOLS;

/// The service type clients browse for.
///
/// Four characters, well inside the fifteen RFC 6763 allows. A picker shows the
/// *instance* name, so the type needs no branding, and the headroom leaves room
/// for a sibling like `_mbrc-http._tcp`.
///
/// **Permanent.** A client that ships against it goes blind if it changes.
pub const SERVICE_TYPE: &str = "_mbrc._tcp.local.";

/// How often the interface set is re-checked. mDNS advertises addresses, so a
/// laptop moving from Wi-Fi to a dock has to re-register or it is advertising
/// somewhere it no longer is. Slow enough to be free, quick enough that nobody
/// waits on it.
const INTERFACE_POLL: Duration = Duration::from_secs(30);

/// How long to give the goodbye packet before tearing the daemon down.
const GOODBYE_GRACE: Duration = Duration::from_millis(500);

/// One thing to advertise. Today there is exactly one; the shape is here so a
/// second endpoint is an entry rather than a redesign.
struct Service {
    service_type: &'static str,
    port: u16,
    txt: HashMap<String, String>,
}

/// Advertises until `shutdown` is signalled, then withdraw.
pub async fn run(tcp_port: u16, shutdown: Arc<Notify>) {
    let daemon = match ServiceDaemon::new() {
        Ok(daemon) => daemon,
        Err(e) => {
            tracing::warn!(error = %e, "mDNS disabled (daemon failed to start)");
            return;
        }
    };

    let instance = instance_name();
    let mut addresses = advertised_addresses();
    if addresses.is_empty() {
        // Nothing to point anyone at. Not fatal - an interface may appear later,
        // and the poll below will pick it up.
        tracing::debug!("mDNS has no advertisable address yet");
    }

    let services = services(tcp_port);
    let mut registered = register_all(&daemon, &instance, &addresses, &services);
    tracing::info!(
        instance = %instance,
        service = SERVICE_TYPE,
        port = tcp_port,
        addresses = addresses.len(),
        "mDNS advertisement started"
    );

    // Pinned outside the loop: `Notify` wakes only the waiters registered when
    // it fires, so re-registering could miss a shutdown and leak the daemon.
    let notified = shutdown.notified();
    tokio::pin!(notified);

    loop {
        tokio::select! {
            _ = &mut notified => break,
            _ = tokio::time::sleep(INTERFACE_POLL) => {
                let current = advertised_addresses();
                if current == addresses {
                    continue;
                }
                tracing::info!(
                    was = addresses.len(),
                    now = current.len(),
                    "mDNS re-registering (the interface set changed)"
                );
                withdraw(&daemon, &registered).await;
                addresses = current;
                registered = register_all(&daemon, &instance, &addresses, &services);
            }
        }
    }

    withdraw(&daemon, &registered).await;
    if let Err(e) = daemon.shutdown() {
        tracing::debug!(error = %e, "mDNS daemon shutdown failed");
    }
    tracing::info!("mDNS advertisement stopped");
}

/// What this host publishes. The TXT keys are identity plus one advisory hint:
/// `protocol` lets a client that only speaks one version filter before
/// connecting, but the handshake stays authoritative - a stale TXT must never be
/// able to talk a client out of a server it could have used.
fn services(tcp_port: u16) -> Vec<Service> {
    let mut txt = HashMap::new();
    txt.insert("protocol".to_owned(), advertised_protocols());
    txt.insert(
        "version".to_owned(),
        crate::updates::CORE_VERSION.to_owned(),
    );
    txt.insert("name".to_owned(), crate::discovery::hostname());

    vec![Service {
        service_type: SERVICE_TYPE,
        port: tcp_port,
        txt,
    }]
}

/// The protocols reachable on the command port, comma-separated (`"4,5,6"`).
fn advertised_protocols() -> String {
    ADVERTISED_PROTOCOLS
        .iter()
        .map(|v| v.to_string())
        .collect::<Vec<_>>()
        .join(",")
}

/// Registers everything, returning the full names of what actually took. A
/// registration that fails is logged and skipped rather than aborting the rest:
/// one bad service should not cost the others.
fn register_all(
    daemon: &ServiceDaemon,
    instance: &str,
    addresses: &[IpAddr],
    services: &[Service],
) -> Vec<String> {
    services
        .iter()
        .filter_map(|service| match info(service, instance, addresses) {
            Ok(info) => {
                let fullname = info.get_fullname().to_owned();
                match daemon.register(info) {
                    Ok(()) => Some(fullname),
                    Err(e) => {
                        tracing::warn!(error = %e, service = service.service_type, "mDNS registration refused");
                        None
                    }
                }
            }
            Err(e) => {
                tracing::warn!(error = %e, service = service.service_type, "mDNS service record rejected");
                None
            }
        })
        .collect()
}

/// Builds one service record. The host name is this machine under `.local.`,
/// which is what the SRV record points at and what the A records answer for.
fn info(service: &Service, instance: &str, addresses: &[IpAddr]) -> mdns_sd::Result<ServiceInfo> {
    ServiceInfo::new(
        service.service_type,
        instance,
        &host_name(instance),
        addresses,
        service.port,
        Some(service.txt.clone()),
    )
}

/// Sends the goodbye packets and give them a moment to leave, so browsers drop
/// the instance now rather than when its TTL runs out.
async fn withdraw(daemon: &ServiceDaemon, registered: &[String]) {
    if registered.is_empty() {
        return;
    }
    for fullname in registered {
        match daemon.unregister(fullname) {
            Ok(_) => tracing::debug!(%fullname, "mDNS goodbye sent"),
            Err(e) => tracing::debug!(%fullname, error = %e, "mDNS unregister failed"),
        }
    }
    tokio::time::sleep(GOODBYE_GRACE).await;
}

/// The instance name browsers show. The machine name, as the custom responder
/// already uses - mDNS resolves collisions itself by suffixing, so two MusicBee
/// hosts on one LAN need nothing from us.
fn instance_name() -> String {
    crate::discovery::hostname()
}

/// The `.local.` host name for the SRV target, derived from the instance name.
/// Anything that is not a DNS label character becomes `-`: a machine name is
/// free-form, a host name is not.
fn host_name(instance: &str) -> String {
    let sanitized: String = instance
        .chars()
        .map(|c| {
            if c.is_ascii_alphanumeric() || c == '-' {
                c
            } else {
                '-'
            }
        })
        .collect();
    let trimmed = sanitized.trim_matches('-');
    let label = if trimmed.is_empty() {
        "musicbee"
    } else {
        trimmed
    };
    format!("{label}.local.")
}

/// The addresses to publish: the same set the custom responder considers and the
/// settings panel lists, so "where can this be reached" has one answer.
fn advertised_addresses() -> Vec<IpAddr> {
    usable_ipv4_ifaces()
        .into_iter()
        .map(|(ip, _mask)| IpAddr::V4(ip))
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    // Nothing here starts a ServiceDaemon: that would bind UDP 5353 and emit
    // multicast, which on Windows means a firewall prompt and a stale rule per
    // test binary. Building the records proves the shape; the daemon is proved
    // by running the plugin.

    #[test]
    fn the_service_type_fits_the_dns_sd_limit() {
        // RFC 6763 allows at most 15 characters, underscore excluded, and this is
        // the value clients ship against.
        let name = SERVICE_TYPE
            .strip_prefix('_')
            .and_then(|s| s.split('.').next())
            .expect("a leading-underscore service label");
        assert_eq!(name, "mbrc");
        assert!(name.len() <= 15, "{name} is {} characters", name.len());
        assert!(SERVICE_TYPE.ends_with("._tcp.local."));
    }

    #[test]
    fn the_record_carries_the_port_and_the_txt_keys() {
        let services = services(3000);
        let service = services.first().expect("one service today");
        assert_eq!(service.port, 3000);
        assert_eq!(
            service.txt.get("protocol").map(String::as_str),
            Some("4,5,6")
        );
        assert!(service.txt.contains_key("version"));
        assert!(service.txt.contains_key("name"));
        // `path` is reserved by DNS-SD convention for HTTP-ish types; publishing
        // one here would mean something we do not mean.
        assert!(!service.txt.contains_key("path"));
    }

    #[test]
    fn the_advertised_protocols_are_the_ones_the_port_serves() {
        let advertised: Vec<u8> = advertised_protocols()
            .split(',')
            .map(|v| v.parse().expect("a numeric version"))
            .collect();
        assert_eq!(advertised, ADVERTISED_PROTOCOLS);
    }

    #[test]
    fn host_names_are_dns_labels() {
        assert_eq!(host_name("LIVING-ROOM-PC"), "LIVING-ROOM-PC.local.");
        // A machine name is free-form; a host name is not.
        assert_eq!(host_name("Kelsos' PC"), "Kelsos--PC.local.");
        assert_eq!(host_name("_"), "musicbee.local.");
        assert_eq!(host_name(""), "musicbee.local.");
    }

    #[test]
    fn a_record_builds_from_real_inputs() {
        let services = services(3000);
        let addresses = vec![IpAddr::V4("192.168.1.20".parse().unwrap())];
        let info = info(&services[0], "TEST-PC", &addresses).expect("a valid record");
        assert_eq!(info.get_port(), 3000);
        assert_eq!(info.get_hostname(), "TEST-PC.local.");
        assert!(info.get_fullname().starts_with("TEST-PC."));
        assert!(info.get_fullname().ends_with(SERVICE_TYPE));
    }
}
