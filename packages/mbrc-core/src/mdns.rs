//! mDNS / DNS-SD advertisement, alongside the custom UDP responder (#160).
//!
//! [`discovery`](crate::discovery) is not going anywhere - every shipped client
//! depends on it. What it cannot do is be found by anything not written against
//! it, and both mobile platforms have DNS-SD browsing built in (`NsdManager`,
//! `NWBrowser`), as does every diagnostic tool on the LAN. This publishes the
//! same facts through the standard mechanism.
//!
//! Four things here are deliberate:
//!
//! - **Best-effort, exactly like the custom responder.** A daemon that will not
//!   start, or a registration that is refused, logs a warning and leaves. Failing
//!   to advertise must never be able to stop the plugin serving clients.
//! - **A list of services, not one.** DNS-SD is per-service and one daemon can
//!   hold several, so a second endpoint later (an HTTP server, say) is another
//!   entry here rather than a rewrite.
//! - **The addresses are ours, not the crate's.** `mdns-sd` can fill in every
//!   address it finds; we hand it [`usable_ipv4_ifaces`] instead, so what is
//!   advertised is the same set the panel's "Reachable at" row shows. mDNS has
//!   no client-subnet hint, so unlike the custom responder we cannot pick *the*
//!   reachable address - but we can at least not publish loopback and APIPA.
//! - **Goodbye on the way out.** A stale instance sitting in every browser on the
//!   LAN until its TTL expires is worse than never having advertised.

use std::collections::HashMap;
use std::net::IpAddr;
use std::sync::Arc;
use std::time::Duration;

use mdns_sd::{ServiceDaemon, ServiceInfo};
use tokio::sync::Notify;

use crate::discovery::usable_ipv4_ifaces;
use crate::protocol::version::SUPPORTED_VERSIONS;

/// The service type clients browse for.
///
/// Four characters, well inside the fifteen RFC 6763 allows for a service name -
/// `_musicbee-remote` would have sat exactly on the limit with no headroom. What
/// a person reads in a picker is the *instance* name, which is this machine's
/// name, so the type does not have to carry the branding. It also leaves room
/// for a sibling type later: `_mbrc-http._tcp` fits, `_musicbeeremote-http`
/// would not.
///
/// **Permanent.** Once a client ships against it, changing it makes that client
/// blind to every server that follows us.
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

/// Advertise until `shutdown` is signalled, then withdraw.
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

    // Pinned once outside the loop rather than created per iteration: `Notify`
    // wakes the waiters registered at the moment it fires and stores nothing, so
    // a notification arriving while this task was busy re-registering would be
    // missed by a fresh `notified()` - and a missed shutdown here leaks the
    // daemon thread, which would go on advertising a server that has stopped.
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

/// The handshake versions this core accepts, comma-separated (`"4,5"`).
fn advertised_protocols() -> String {
    SUPPORTED_VERSIONS
        .iter()
        .map(|v| v.to_string())
        .collect::<Vec<_>>()
        .join(",")
}

/// Register everything, returning the full names of what actually took. A
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

/// Build one service record. The host name is this machine under `.local.`,
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

/// Send the goodbye packets and give them a moment to leave, so browsers drop
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
        // RFC 6763 allows at most 15 characters in a service name, underscore
        // excluded. This is the value clients ship against, so it is pinned by a
        // test rather than left to a careless edit.
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
        assert_eq!(service.txt.get("protocol").map(String::as_str), Some("4,5"));
        assert!(service.txt.contains_key("version"));
        assert!(service.txt.contains_key("name"));
        // `path` is reserved by DNS-SD convention for HTTP-ish types; publishing
        // one here would mean something we do not mean.
        assert!(!service.txt.contains_key("path"));
    }

    #[test]
    fn the_advertised_protocols_are_the_supported_ones() {
        let advertised: Vec<u8> = advertised_protocols()
            .split(',')
            .map(|v| v.parse().expect("a numeric version"))
            .collect();
        assert_eq!(advertised, SUPPORTED_VERSIONS);
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
