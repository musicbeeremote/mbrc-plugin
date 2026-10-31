//! The connect link and its QR code (#23): how the Android app learns where the
//! server is when discovery cannot reach it.
//!
//! The link is a verified App Link, `https://mbrc.kelsos.net/connect#v=1&h=...`.
//! The payload sits in the fragment, so the web server never sees the LAN
//! addresses. App 1.7.1 and later opens it directly; anything older lands on
//! the site's `/connect` page, which lists the addresses. The schema (v1) is
//! shared with the app's parser in musicbeeremote/mbrc#66 and is frozen once a
//! released client reads it: add optional keys, never change existing ones.
//!
//! Only addresses the app accepts go in (RFC 1918 and the 100.64/10 overlay
//! range), ordered by how likely a phone can reach them, because the app tries
//! them in order and Hyper-V, WSL and Docker adapters sit in 172.16/12.

use std::io::Cursor;
use std::net::Ipv4Addr;

use image::{ExtendedColorType, ImageEncoder, codecs::png::PngEncoder};
use qrcodegen::{QrCode, QrCodeEcc};

use crate::ffi::dtos::ConnectQr;

/// The App Link the app's intent filter and the site's fallback page answer.
pub const LINK_BASE: &str = "https://mbrc.kelsos.net/connect";
/// The payload schema version, the link's `v`.
pub const SCHEMA_VERSION: u32 = 1;
/// The most addresses the app accepts in `h`; it rejects a link with more.
pub const MAX_HOSTS: usize = 8;
/// The app keeps this many characters of `n`.
pub const MAX_NAME_CHARS: usize = 64;

const QUIET_ZONE_MODULES: i32 = 4;
const PIXELS_PER_MODULE: i32 = 6;

/// What a link carries, before encoding.
#[derive(Debug, Clone, Copy)]
pub struct LinkParts<'a> {
    /// Ordered as [`order_hosts`] returns them.
    pub hosts: &'a [Ipv4Addr],
    pub port: u16,
    pub server_id: Option<&'a str>,
    pub name: Option<&'a str>,
    /// A six-digit pairing code. The wizard and settings QR send none.
    pub code: Option<&'a str>,
}

/// Encodes `parts` as the v1 link, keys in schema order: `v h p id n c`.
pub fn link(parts: &LinkParts) -> String {
    let hosts: Vec<String> = parts.hosts.iter().map(Ipv4Addr::to_string).collect();
    let mut out = format!(
        "{LINK_BASE}#v={SCHEMA_VERSION}&h={}&p={}",
        hosts.join(","),
        parts.port
    );
    if let Some(id) = parts.server_id.filter(|id| !id.is_empty()) {
        out.push_str("&id=");
        out.push_str(&percent_encode(id));
    }
    let name: String = parts
        .name
        .unwrap_or_default()
        .trim()
        .chars()
        .take(MAX_NAME_CHARS)
        .collect();
    if !name.is_empty() {
        out.push_str("&n=");
        out.push_str(&percent_encode(&name));
    }
    if let Some(code) = parts.code {
        out.push_str("&c=");
        out.push_str(&percent_encode(code));
    }
    out
}

/// The addresses the app accepts, most reachable first, at most [`MAX_HOSTS`].
pub fn order_hosts(candidates: impl IntoIterator<Item = Ipv4Addr>) -> Vec<Ipv4Addr> {
    let mut hosts: Vec<(u8, Ipv4Addr)> = Vec::new();
    for ip in candidates {
        if let Some(rank) = reachability_rank(ip)
            && !hosts.iter().any(|(_, seen)| *seen == ip)
        {
            hosts.push((rank, ip));
        }
    }
    hosts.sort_by_key(|(rank, _)| *rank);
    hosts
        .into_iter()
        .map(|(_, ip)| ip)
        .take(MAX_HOSTS)
        .collect()
}

/// Lower is likelier: the home LAN ranges first, then the ranges virtual
/// adapters and overlay VPNs use. `None` for addresses the app rejects.
fn reachability_rank(ip: Ipv4Addr) -> Option<u8> {
    match ip.octets() {
        [192, 168, ..] => Some(0),
        [10, ..] => Some(1),
        [172, b, ..] if (16..=31).contains(&b) => Some(2),
        [100, b, ..] if (64..=127).contains(&b) => Some(3),
        _ => None,
    }
}

/// Percent-encodes everything outside the URL unreserved set, as UTF-8 bytes.
fn percent_encode(value: &str) -> String {
    let mut out = String::with_capacity(value.len());
    for byte in value.bytes() {
        if byte.is_ascii_alphanumeric() || matches!(byte, b'-' | b'.' | b'_' | b'~') {
            out.push(byte as char);
        } else {
            out.push_str(&format!("%{byte:02X}"));
        }
    }
    out
}

/// Encodes `link` at error correction level M.
fn encode(link: &str) -> Option<QrCode> {
    QrCode::encode_text(link, QrCodeEcc::Medium).ok()
}

/// Renders `link` as a black-on-white PNG with a four-module quiet zone.
pub fn render_png(link: &str) -> Option<Vec<u8>> {
    let qr = encode(link)?;
    let side = (qr.size() + 2 * QUIET_ZONE_MODULES) * PIXELS_PER_MODULE;
    let mut pixels = Vec::with_capacity((side * side) as usize);
    for y in 0..side {
        for x in 0..side {
            let module_x = x / PIXELS_PER_MODULE - QUIET_ZONE_MODULES;
            let module_y = y / PIXELS_PER_MODULE - QUIET_ZONE_MODULES;
            pixels.push(if qr.get_module(module_x, module_y) {
                0
            } else {
                255
            });
        }
    }
    let mut png = Vec::new();
    PngEncoder::new(Cursor::new(&mut png))
        .write_image(&pixels, side as u32, side as u32, ExtendedColorType::L8)
        .ok()?;
    Some(png)
}

/// The QR for this server: no link and no image when no candidate address is
/// one a phone could reach.
pub fn connect_qr(
    candidates: impl IntoIterator<Item = Ipv4Addr>,
    port: u16,
    server_id: Option<&str>,
    name: &str,
) -> ConnectQr {
    let hosts = order_hosts(candidates);
    let addresses = hosts.iter().map(Ipv4Addr::to_string).collect();
    if hosts.is_empty() {
        return ConnectQr {
            link: String::new(),
            png: Vec::new(),
            addresses,
            port,
        };
    }
    let link = link(&LinkParts {
        hosts: &hosts,
        port,
        server_id,
        name: Some(name),
        code: None,
    });
    let png = render_png(&link).unwrap_or_default();
    ConnectQr {
        link,
        png,
        addresses,
        port,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const ID: &str = "3f2b8c1e-9d4a-4b6f-8e2d-1a7c5b9e0f34";

    fn ip(text: &str) -> Ipv4Addr {
        text.parse().unwrap()
    }

    fn parts(hosts: &[Ipv4Addr]) -> LinkParts<'_> {
        LinkParts {
            hosts,
            port: 3000,
            server_id: None,
            name: None,
            code: None,
        }
    }

    #[test]
    fn one_host_matches_the_first_canonical_link() {
        let hosts = [ip("192.168.1.5")];
        assert_eq!(
            link(&parts(&hosts)),
            "https://mbrc.kelsos.net/connect#v=1&h=192.168.1.5&p=3000"
        );
    }

    #[test]
    fn two_hosts_with_id_and_name_match_the_second_canonical_link() {
        let hosts = order_hosts([ip("172.20.48.1"), ip("192.168.1.5")]);
        let link = link(&LinkParts {
            server_id: Some(ID),
            name: Some("LIVING-ROOM-PC"),
            ..parts(&hosts)
        });
        assert_eq!(
            link,
            "https://mbrc.kelsos.net/connect#v=1&h=192.168.1.5,172.20.48.1&p=3000\
             &id=3f2b8c1e-9d4a-4b6f-8e2d-1a7c5b9e0f34&n=LIVING-ROOM-PC"
        );
    }

    #[test]
    fn a_name_with_a_space_and_a_code_match_the_third_canonical_link() {
        let hosts = [ip("192.168.1.5")];
        let link = link(&LinkParts {
            server_id: Some(ID),
            name: Some("Den PC"),
            code: Some("482913"),
            ..parts(&hosts)
        });
        assert_eq!(
            link,
            "https://mbrc.kelsos.net/connect#v=1&h=192.168.1.5&p=3000\
             &id=3f2b8c1e-9d4a-4b6f-8e2d-1a7c5b9e0f34&n=Den%20PC&c=482913"
        );
    }

    #[test]
    fn a_non_ascii_name_is_percent_encoded_as_utf8_and_cut_at_64_characters() {
        let hosts = [ip("192.168.1.5")];
        let long = format!("Ωmega&{}", "x".repeat(80));
        let link = link(&LinkParts {
            name: Some(&long),
            ..parts(&hosts)
        });
        let name = link.split("&n=").nth(1).unwrap();
        assert!(name.starts_with("%CE%A9mega%26x"));
        assert_eq!(
            name.matches('x').count(),
            MAX_NAME_CHARS - "Ωmega&".chars().count()
        );
    }

    #[test]
    fn a_blank_name_and_an_empty_id_are_left_out() {
        let hosts = [ip("192.168.1.5")];
        let link = link(&LinkParts {
            server_id: Some(""),
            name: Some("   "),
            ..parts(&hosts)
        });
        assert_eq!(
            link,
            "https://mbrc.kelsos.net/connect#v=1&h=192.168.1.5&p=3000"
        );
    }

    #[test]
    fn hosts_are_ordered_home_lan_first_and_virtual_adapter_ranges_last() {
        let ordered = order_hosts([
            ip("100.101.1.2"),
            ip("172.20.48.1"),
            ip("10.0.0.3"),
            ip("192.168.1.5"),
            ip("172.17.0.1"),
        ]);
        assert_eq!(
            ordered,
            [
                ip("192.168.1.5"),
                ip("10.0.0.3"),
                ip("172.20.48.1"),
                ip("172.17.0.1"),
                ip("100.101.1.2"),
            ]
        );
    }

    #[test]
    fn addresses_the_app_rejects_and_duplicates_are_dropped() {
        let ordered = order_hosts([
            ip("8.8.8.8"),
            ip("172.32.0.1"),
            ip("100.128.0.1"),
            ip("192.168.1.5"),
            ip("192.168.1.5"),
        ]);
        assert_eq!(ordered, [ip("192.168.1.5")]);
    }

    #[test]
    fn at_most_eight_hosts_are_kept_and_the_likeliest_survive() {
        let mut candidates: Vec<Ipv4Addr> = (1..=6).map(|n| Ipv4Addr::new(172, 16, 0, n)).collect();
        candidates.extend((1..=4).map(|n| Ipv4Addr::new(192, 168, 0, n)));
        let ordered = order_hosts(candidates);
        assert_eq!(ordered.len(), MAX_HOSTS);
        assert!(ordered[..4].iter().all(|ip| ip.octets()[0] == 192));
    }

    #[test]
    fn no_reachable_address_gives_no_link_and_no_image() {
        let qr = connect_qr([ip("8.8.8.8")], 3000, Some(ID), "PC");
        assert!(qr.link.is_empty());
        assert!(qr.png.is_empty());
        assert!(qr.addresses.is_empty());
        assert_eq!(qr.port, 3000);
    }

    fn version_for(host_count: usize, name_chars: usize, port: u16) -> u8 {
        let hosts: Vec<Ipv4Addr> = (0..host_count as u8)
            .map(|n| Ipv4Addr::new(192, 168, 100, 200 + n))
            .collect();
        let name = "N".repeat(name_chars);
        let link = link(&LinkParts {
            hosts: &hosts,
            port,
            server_id: Some(ID),
            name: Some(&name),
            code: None,
        });
        encode(&link).unwrap().version().value()
    }

    /// Four adapters and a NetBIOS-length (15 character) machine name.
    #[test]
    fn a_typical_link_fits_a_version_9_symbol() {
        assert!(version_for(4, 15, 3000) <= 9);
    }

    /// Eight hosts and a 64-character name: 65 modules, 438 px with the quiet zone.
    #[test]
    fn the_worst_case_link_fits_a_version_12_symbol() {
        assert!(version_for(MAX_HOSTS, MAX_NAME_CHARS, 65535) <= 12);
    }

    #[test]
    fn the_image_is_a_square_png_with_the_quiet_zone() {
        let qr = connect_qr([ip("192.168.1.5")], 3000, Some(ID), "LIVING-ROOM-PC");
        let image = image::load_from_memory(&qr.png).unwrap().to_luma8();
        let modules = encode(&qr.link).unwrap().size();
        let side = ((modules + 2 * QUIET_ZONE_MODULES) * PIXELS_PER_MODULE) as u32;
        assert_eq!(image.dimensions(), (side, side));
        let quiet = (QUIET_ZONE_MODULES * PIXELS_PER_MODULE) as u32;
        assert!((0..side).all(|i| image.get_pixel(i, quiet - 1).0 == [255]));
        assert_eq!(
            image.get_pixel(quiet, quiet).0,
            [0],
            "finder pattern corner"
        );
    }
}
