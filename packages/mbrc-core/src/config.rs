//! Rust-canonical runtime settings, read from `core_settings.json` in the
//! storage directory handed to `mbrc_initialize`.
//!
//! This is the single source of truth for runtime config. At cutover the C#
//! `Configure()` UI edits this file and signals a reload; there is no parallel
//! C# settings store for these values.

use std::net::{IpAddr, Ipv4Addr};
use std::path::Path;

use serde::{Deserialize, Serialize};

fn default_port() -> u16 {
    3000
}

fn default_last_octet_max() -> u32 {
    254
}

fn default_search_source() -> i32 {
    1 // SearchSource.Library
}

/// Which clients may connect, mirroring the shipped plugin's three modes. The
/// server checks each inbound peer against the active mode (loopback is always
/// allowed). Serialized lowercase to match the panel/JSON.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Deserialize, Serialize, Default)]
#[serde(rename_all = "lowercase")]
pub enum FilterMode {
    /// Allow every client (the default).
    #[default]
    All,
    /// Allow a contiguous last-octet range on one /24 (`base_ip` .. `.last_octet_max`).
    Range,
    /// Allow only the exact addresses in `allowed_addresses`.
    Specific,
}

/// How verbose the core's log is. Serialized lowercase to match the settings
/// JSON and the panel's select. The host maps this to a tracing filter directive
/// and pushes it via `mbrc_set_log_level`; the core also reads it directly to
/// gate its most verbose per-item traces.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Deserialize, Serialize, Default)]
#[serde(rename_all = "lowercase")]
pub enum LogLevel {
    /// Info and above (the default, "Normal" in the panel).
    #[default]
    Info,
    /// Adds the core's debug logs (frame trace, etc.).
    Debug,
    /// Everything, including per-item traces like per-cover build timing.
    Trace,
}

impl LogLevel {
    /// Whether the most verbose per-item traces (e.g. the per-cover cover-build
    /// timing) should emit. Reserved for `Trace` so `Debug` stays readable.
    pub fn is_trace(self) -> bool {
        matches!(self, LogLevel::Trace)
    }
}

fn default_ping_interval_secs() -> u64 {
    15
}

fn default_unhandshaked_timeout_secs() -> u64 {
    60
}

fn default_max_conns_per_client() -> usize {
    20
}

fn default_max_conns_per_ip() -> usize {
    40
}

fn default_tcp_keepalive_secs() -> u64 {
    45
}

/// The core's runtime configuration. Every field has a default so a missing or
/// partial `core_settings.json` still yields a usable config.
#[derive(Debug, Clone, Deserialize, Serialize)]
#[serde(default)]
pub struct Config {
    /// TCP port the command server listens on.
    pub port: u16,
    /// The client-address filtering mode (`all` / `range` / `specific`).
    pub filter_mode: FilterMode,
    /// Range mode: the base IPv4 whose first three octets bound the /24; its
    /// last octet is the low end of the allowed range.
    pub base_ip: String,
    /// Range mode: the inclusive high end of the allowed last octet.
    pub last_octet_max: u32,
    /// Specific mode: the exact client addresses allowed to connect.
    pub allowed_addresses: Vec<String>,
    /// Which MusicBee sources library search/browse targets, as the C#
    /// `SearchSource` flags value (Library=1, Inbox=2, Podcasts=4, ...). C#
    /// reads it from its settings snapshot and casts back. Default 1 (Library).
    pub search_source: i32,
    /// Whether the host adds a Windows firewall rule on save (host-facing
    /// preference; persisted here, acted on C#-side via firewall-utility).
    pub update_firewall: bool,
    /// How verbose the core log is (`info` / `debug` / `trace`).
    pub log_level: LogLevel,
    /// How often the server sends a keepalive `ping` to each broadcast
    /// subscriber (the main socket), in seconds. Auxiliary request/response
    /// sockets are never pinged (matching the shipped C# plugin).
    #[serde(default = "default_ping_interval_secs")]
    pub ping_interval_secs: u64,
    /// Close a connection that connected but never completed the handshake after
    /// this many seconds (HTTP `client_header_timeout`) - bounds sockets that
    /// negotiate nothing.
    #[serde(default = "default_unhandshaked_timeout_secs")]
    pub unhandshaked_timeout_secs: u64,
    /// Max concurrent connections a single `client_id` may hold; the newest over
    /// the cap is rejected. A leak backstop for grouped clients (Android v4).
    #[serde(default = "default_max_conns_per_client")]
    pub max_conns_per_client: usize,
    /// Max concurrent connections from a single source IP (loopback exempt); the
    /// newest over the cap is rejected. Bounds ungrouped clients (iOS, old
    /// Android) and, on a LAN, is effectively a per-device leak bound.
    #[serde(default = "default_max_conns_per_ip")]
    pub max_conns_per_ip: usize,
    /// OS-level TCP keepalive idle time (seconds) set on each accepted socket so
    /// the kernel detects and drops dead half-open connections.
    #[serde(default = "default_tcp_keepalive_secs")]
    pub tcp_keepalive_secs: u64,
    /// The storage directory handed to `mbrc_initialize` (not read from JSON;
    /// set by [`Config::load`]). Roots the on-disk cover cache. Empty in unit
    /// tests that build a `Config` literal - the cover build is skipped then.
    #[serde(skip)]
    pub storage_path: String,
}

impl Default for Config {
    fn default() -> Self {
        Self {
            port: default_port(),
            filter_mode: FilterMode::default(),
            base_ip: String::new(),
            last_octet_max: default_last_octet_max(),
            allowed_addresses: Vec::new(),
            search_source: default_search_source(),
            update_firewall: false,
            log_level: LogLevel::default(),
            ping_interval_secs: default_ping_interval_secs(),
            unhandshaked_timeout_secs: default_unhandshaked_timeout_secs(),
            max_conns_per_client: default_max_conns_per_client(),
            max_conns_per_ip: default_max_conns_per_ip(),
            tcp_keepalive_secs: default_tcp_keepalive_secs(),
            storage_path: String::new(),
        }
    }
}

impl Config {
    /// Load `<storage_path>/core_settings.json`, falling back to defaults if the
    /// file is missing or unparseable (logged, never fatal).
    pub fn load(storage_path: &str) -> Self {
        let path = Path::new(storage_path).join("core_settings.json");
        let mut config = Config::default();
        if let Ok(contents) = std::fs::read_to_string(&path) {
            config = serde_json::from_str(&contents).unwrap_or_else(|e| {
                tracing::warn!(error = %e, "core_settings.json is invalid; using defaults");
                Config::default()
            });
            // Migrate a pre-`log_level` file: an old `debug:true` (and no
            // `log_level` key) maps to Debug. The new file drops `debug` on save.
            if config.log_level == LogLevel::Info {
                if let Ok(v) = serde_json::from_str::<serde_json::Value>(&contents) {
                    if v.get("log_level").is_none()
                        && v.get("debug").and_then(serde_json::Value::as_bool) == Some(true)
                    {
                        config.log_level = LogLevel::Debug;
                    }
                }
            }
        } else {
            tracing::info!("no core_settings.json found; using default config");
        }
        config.storage_path = storage_path.to_string();
        config
    }

    /// Reject settings the core can't safely run with, so a typo in the panel
    /// can't persist a file that bricks the listener on the next init. Called by
    /// the write-settings FFI before anything is written to disk.
    pub fn validate(&self) -> Result<(), String> {
        if self.port == 0 {
            return Err("port must be between 1 and 65535".into());
        }
        if self.filter_mode == FilterMode::Range && self.base_ip.parse::<Ipv4Addr>().is_err() {
            return Err(format!(
                "range mode requires a valid base IPv4 address (got '{}')",
                self.base_ip
            ));
        }
        if self.last_octet_max > 255 {
            return Err("last_octet_max must be between 0 and 255".into());
        }
        Ok(())
    }

    /// Whether a client at `ip` may connect, matching the shipped C#
    /// `SocketServer.IsClientAllowed`: loopback is always allowed; otherwise the
    /// active [`FilterMode`] decides.
    pub fn is_client_allowed(&self, ip: IpAddr) -> bool {
        if ip.is_loopback() {
            return true;
        }
        match self.filter_mode {
            FilterMode::All => true,
            FilterMode::Specific => self
                .allowed_addresses
                .iter()
                .any(|entry| ip_matches_entry(ip, entry)),
            FilterMode::Range => self.ip_in_range(ip),
        }
    }

    /// Range check (IPv4 only): the first three octets must equal `base_ip`'s and
    /// the last octet must be within `[base_ip.last, last_octet_max]`. Matches C#
    /// `IsInAllowedRange`; a non-IPv4 peer or an unparseable `base_ip` is denied.
    fn ip_in_range(&self, ip: IpAddr) -> bool {
        let IpAddr::V4(ip) = ip else {
            return false;
        };
        let Ok(base) = self.base_ip.parse::<Ipv4Addr>() else {
            return false;
        };
        let (ip, base) = (ip.octets(), base.octets());
        if ip[0..3] != base[0..3] {
            return false;
        }
        let last = u32::from(ip[3]);
        last >= u32::from(base[3]) && last <= self.last_octet_max
    }
}

/// Match a peer against one allow-list entry: an exact address, or a CIDR block
/// (`a.b.c.d/prefix`) when the entry contains a slash. Lets the `Specific` mode's
/// list mix single IPs and subnets (the modern alternative to the legacy Range).
fn ip_matches_entry(ip: IpAddr, entry: &str) -> bool {
    match entry.split_once('/') {
        Some((network, prefix)) => ip_in_cidr(ip, network.trim(), prefix.trim()),
        None => entry.trim() == ip.to_string(),
    }
}

/// IPv4 CIDR containment. A non-IPv4 peer, an unparseable network, or a prefix
/// outside `0..=32` does not match. Prefix 0 matches everything in the block.
fn ip_in_cidr(ip: IpAddr, network: &str, prefix: &str) -> bool {
    let (IpAddr::V4(ip), Ok(net)) = (ip, network.parse::<Ipv4Addr>()) else {
        return false;
    };
    let Ok(prefix) = prefix.parse::<u32>() else {
        return false;
    };
    if prefix > 32 {
        return false;
    }
    let mask: u32 = if prefix == 0 {
        0
    } else {
        u32::MAX << (32 - prefix)
    };
    (u32::from(ip) & mask) == (u32::from(net) & mask)
}

/// One-time migration of the shipped plugin's `settings.xml` to the Rust-owned
/// `core_settings.json`. Runs when the new file is absent but the legacy file is
/// present in the same storage dir (`mb_remote/`). Best-effort: any failure
/// leaves the core to start with defaults (logged, never fatal). Settings are
/// Rust-owned, so the core owns this migration too.
pub fn migrate_legacy_settings(storage_path: &str) {
    let dir = Path::new(storage_path);
    let new_path = dir.join("core_settings.json");
    if new_path.exists() {
        return; // already migrated / configured by the core
    }
    let Ok(xml) = std::fs::read_to_string(dir.join("settings.xml")) else {
        return; // no legacy settings to migrate
    };

    let config = config_from_legacy_xml(&xml);
    match serde_json::to_string_pretty(&config) {
        Ok(json) => match std::fs::write(&new_path, json) {
            Ok(()) => tracing::info!("migrated legacy settings.xml -> core_settings.json"),
            Err(e) => tracing::warn!(error = %e, "settings migration: write failed"),
        },
        Err(e) => tracing::warn!(error = %e, "settings migration: serialize failed"),
    }
}

/// Build a [`Config`] from the flat legacy `<mbremote>` XML. Missing/unknown
/// nodes fall back to defaults. The legacy format is our own (flat, ASCII
/// values, no attributes or entities), so a targeted tag extractor suffices.
fn config_from_legacy_xml(xml: &str) -> Config {
    let mut config = Config::default();

    if let Some(port) = xml_tag(xml, "port").and_then(|v| v.parse::<u16>().ok()) {
        config.port = port;
    }
    config.filter_mode = match xml_tag(xml, "selection") {
        Some("Range") => FilterMode::Range,
        Some("Specific") => FilterMode::Specific,
        _ => FilterMode::All,
    };
    // `values` is selection-dependent: Range = "baseIp,lastOctet"; Specific =
    // "addr1,addr2,...," (trailing comma). All ignores it.
    if let Some(values) = xml_tag(xml, "values") {
        match config.filter_mode {
            FilterMode::Range => {
                let mut parts = values.split(',');
                if let Some(base) = parts.next() {
                    config.base_ip = base.trim().to_string();
                }
                if let Some(max) = parts.next().and_then(|v| v.trim().parse::<u32>().ok()) {
                    config.last_octet_max = max;
                }
            }
            FilterMode::Specific => {
                config.allowed_addresses = values
                    .split(',')
                    .map(str::trim)
                    .filter(|s| !s.is_empty())
                    .map(String::from)
                    .collect();
            }
            FilterMode::All => {}
        }
    }
    if let Some(source) = xml_tag(xml, "source").and_then(|v| v.parse::<i32>().ok()) {
        config.search_source = source;
    }
    config.log_level = if xml_bool(xml, "logs_enabled") {
        LogLevel::Debug
    } else {
        LogLevel::Info
    };
    config.update_firewall = xml_bool(xml, "update_firewall");

    config
}

/// Inner text of the first `<tag>...</tag>` in a flat XML doc, trimmed.
fn xml_tag<'a>(xml: &'a str, tag: &str) -> Option<&'a str> {
    let open = format!("<{tag}>");
    let close = format!("</{tag}>");
    let start = xml.find(&open)? + open.len();
    let end = xml[start..].find(&close)? + start;
    Some(xml[start..end].trim())
}

/// A legacy boolean node (`true`/`false`, case-insensitive per C# `bool.ToString`);
/// missing = false.
fn xml_bool(xml: &str, tag: &str) -> bool {
    xml_tag(xml, tag).is_some_and(|v| v.eq_ignore_ascii_case("true"))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn empty_json_yields_defaults() {
        let c: Config = serde_json::from_str("{}").unwrap();
        assert_eq!(c.port, 3000);
        assert!(c.allowed_addresses.is_empty());
        assert_eq!(c.log_level, LogLevel::Info);
    }

    #[test]
    fn partial_json_overrides_only_present_fields() {
        let c: Config = serde_json::from_str(r#"{"port":5000,"log_level":"trace"}"#).unwrap();
        assert_eq!(c.port, 5000);
        assert_eq!(c.log_level, LogLevel::Trace);
        assert!(c.log_level.is_trace());
        assert!(c.allowed_addresses.is_empty());
    }

    #[test]
    fn legacy_debug_bool_migrates_to_debug_level() {
        // An old file with `debug:true` and no `log_level`: load() maps it to
        // Debug, and the loaded config no longer carries the legacy key.
        let dir = std::env::temp_dir().join("mbrc-cfg-legacy-debug");
        std::fs::create_dir_all(&dir).unwrap();
        std::fs::write(
            dir.join("core_settings.json"),
            r#"{"port":3000,"debug":true}"#,
        )
        .unwrap();
        let c = Config::load(dir.to_str().unwrap());
        assert_eq!(c.log_level, LogLevel::Debug);
        // An explicit log_level always wins over a stray legacy debug.
        std::fs::write(
            dir.join("core_settings.json"),
            r#"{"debug":true,"log_level":"info"}"#,
        )
        .unwrap();
        assert_eq!(
            Config::load(dir.to_str().unwrap()).log_level,
            LogLevel::Info
        );
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn missing_file_falls_back_to_defaults() {
        let c = Config::load("/no/such/dir/definitely/missing");
        assert_eq!(c.port, 3000);
    }

    fn ip(s: &str) -> IpAddr {
        s.parse().unwrap()
    }

    #[test]
    fn loopback_is_always_allowed_regardless_of_mode() {
        let c = Config {
            filter_mode: FilterMode::Specific,
            allowed_addresses: vec![],
            ..Config::default()
        };
        assert!(c.is_client_allowed(ip("127.0.0.1")));
        assert!(c.is_client_allowed(ip("::1")));
    }

    #[test]
    fn all_mode_allows_everyone() {
        let c = Config::default();
        assert!(c.is_client_allowed(ip("10.1.2.3")));
    }

    #[test]
    fn specific_mode_matches_exact_addresses() {
        let c = Config {
            filter_mode: FilterMode::Specific,
            allowed_addresses: vec!["192.168.1.50".into(), "192.168.1.51".into()],
            ..Config::default()
        };
        assert!(c.is_client_allowed(ip("192.168.1.50")));
        assert!(!c.is_client_allowed(ip("192.168.1.52")));
    }

    #[test]
    fn specific_mode_matches_cidr_entries() {
        // The allow-list mixes an exact IP and two subnets.
        let c = Config {
            filter_mode: FilterMode::Specific,
            allowed_addresses: vec![
                "10.0.0.5".into(),
                "192.168.1.0/24".into(),
                "172.16.0.0/16".into(),
            ],
            ..Config::default()
        };
        assert!(c.is_client_allowed(ip("10.0.0.5"))); // exact
        assert!(c.is_client_allowed(ip("192.168.1.200"))); // in /24
        assert!(!c.is_client_allowed(ip("192.168.2.1"))); // outside /24
        assert!(c.is_client_allowed(ip("172.16.99.99"))); // in /16
        assert!(!c.is_client_allowed(ip("172.17.0.1"))); // outside /16
        assert!(!c.is_client_allowed(ip("10.0.0.6"))); // not listed
    }

    #[test]
    fn cidr_rejects_bad_prefix_and_non_ipv4() {
        let c = Config {
            filter_mode: FilterMode::Specific,
            allowed_addresses: vec!["192.168.1.0/33".into(), "192.168.1.0/notanum".into()],
            ..Config::default()
        };
        assert!(!c.is_client_allowed(ip("192.168.1.5")));
    }

    #[test]
    fn range_mode_matches_csharp_semantics() {
        // Allow 192.168.1.10 .. 192.168.1.100 (first 3 octets fixed).
        let c = Config {
            filter_mode: FilterMode::Range,
            base_ip: "192.168.1.10".into(),
            last_octet_max: 100,
            ..Config::default()
        };
        assert!(!c.is_client_allowed(ip("192.168.1.9"))); // below base last octet
        assert!(c.is_client_allowed(ip("192.168.1.10"))); // low bound inclusive
        assert!(c.is_client_allowed(ip("192.168.1.100"))); // high bound inclusive
        assert!(!c.is_client_allowed(ip("192.168.1.101"))); // above max
        assert!(!c.is_client_allowed(ip("192.168.2.50"))); // different /24
    }

    #[test]
    fn range_mode_denies_non_ipv4_and_bad_base() {
        let c = Config {
            filter_mode: FilterMode::Range,
            base_ip: "not-an-ip".into(),
            last_octet_max: 254,
            ..Config::default()
        };
        assert!(!c.is_client_allowed(ip("192.168.1.5")));
    }

    #[test]
    fn legacy_xml_migrates_to_config() {
        let xml = r#"<?xml version="1.0" encoding="utf-8"?>
<mbremote>
  <port>3456</port>
  <selection>Specific</selection>
  <values>192.168.1.10,192.168.1.11,</values>
  <source>2</source>
  <logs_enabled>True</logs_enabled>
  <update_firewall>True</update_firewall>
  <lastrunversion>1.4.1.0</lastrunversion>
</mbremote>"#;
        let c = config_from_legacy_xml(xml);
        assert_eq!(c.port, 3456);
        assert_eq!(c.filter_mode, FilterMode::Specific);
        assert_eq!(c.allowed_addresses, vec!["192.168.1.10", "192.168.1.11"]);
        assert_eq!(c.search_source, 2);
        assert_eq!(c.log_level, LogLevel::Debug);
        assert!(c.update_firewall);
    }

    #[test]
    fn legacy_xml_range_selection_parses_base_and_octet() {
        let xml =
            "<mbremote><selection>Range</selection><values>192.168.1.5,120</values></mbremote>";
        let c = config_from_legacy_xml(xml);
        assert_eq!(c.filter_mode, FilterMode::Range);
        assert_eq!(c.base_ip, "192.168.1.5");
        assert_eq!(c.last_octet_max, 120);
    }

    #[test]
    fn config_serializes_new_fields() {
        let json = serde_json::to_string(&Config::default()).unwrap();
        assert!(json.contains("\"filter_mode\":\"all\""));
        assert!(json.contains("\"last_octet_max\":254"));
        assert!(json.contains("\"log_level\":\"info\""));
        // storage_path is skipped (not part of the on-disk settings), and the
        // legacy debug bool is never written back.
        assert!(!json.contains("storage_path"));
        assert!(!json.contains("debug"));
    }
}
