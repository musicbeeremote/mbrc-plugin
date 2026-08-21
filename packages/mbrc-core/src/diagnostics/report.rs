//! `report.json`: the environment and core state a bug report needs, assembled
//! in one place.
//!
//! Nothing here is new state - every value already existed for the settings
//! panel, the update flow, or the caches. The point is that a maintainer
//! reading an issue should not have to ask for any of it, and a user should not
//! have to know it exists.

use serde_json::{json, Value};
use time::{format_description::well_known::Rfc3339, OffsetDateTime};

use crate::diagnostics::redact;
use crate::ffi::dtos::CaptureEnvEntry;
use crate::state::Core;

/// Assemble the report for a capture that began at `started_unix_ms`.
///
/// Takes `&Core` rather than reaching for the global state so it stays testable
/// and so the caller keeps control of the lock: this reads a fair amount, and
/// none of it should happen with the core mutex held.
pub fn build(core: &Core, host_env: &[CaptureEnvEntry], started_unix_ms: i64) -> Value {
    json!({
        "generated_at": now_rfc3339(),
        "capture": {
            "started_unix_ms": started_unix_ms,
            "host_environment": host_environment(host_env),
        },
        "versions": versions(core),
        "settings": settings(core),
        "listening": listening(core),
        "caches": caches(core),
        "blocked_connections": core.blocked.recent(),
        "update": update(core),
        "process": {
            "rss_mib": crate::logging::rss_mib(),
        },
    })
}

/// The report's own timestamp. Empty rather than a fake value if the clock
/// cannot be formatted - an absent timestamp is honest, a wrong one is not.
fn now_rfc3339() -> String {
    OffsetDateTime::now_utc()
        .format(&Rfc3339)
        .unwrap_or_default()
}

/// What only the host can see, flattened from the key/value pairs C# sent
/// (MusicBee build, Windows version, CLR version).
fn host_environment(entries: &[CaptureEnvEntry]) -> Value {
    let mut map = serde_json::Map::new();
    for entry in entries {
        map.insert(entry.key.clone(), Value::String(entry.value.clone()));
    }
    Value::Object(map)
}

/// Every version that identifies this build. `plugin` comes from the host and
/// can legitimately be absent (a provider error), which is itself worth seeing.
fn versions(core: &Core) -> Value {
    json!({
        "core": crate::updates::CORE_VERSION,
        "plugin": core.providers.plugin_version().ok(),
        "abi": crate::ffi::types::MBRC_ABI_VERSION,
        "protocol_supported": crate::protocol::version::SUPPORTED_VERSIONS,
    })
}

/// The user's settings, through the bundle's redaction policy. A serialization
/// failure yields null rather than sinking the whole report.
fn settings(core: &Core) -> Value {
    serde_json::to_value(&core.config)
        .map(redact::settings)
        .unwrap_or(Value::Null)
}

/// What a client could reach this machine on - the single most common thing a
/// "the app can't find my PC" report is missing.
fn listening(core: &Core) -> Value {
    let addresses: Vec<String> = crate::discovery::usable_ipv4_ifaces()
        .into_iter()
        .map(|(ip, _mask)| ip.to_string())
        .collect();
    json!({
        "port": core.config.port,
        "bind_address": core.config.bind_address,
        "addresses": addresses,
        "mdns_enabled": core.config.mdns_enabled,
    })
}

/// Cache health, the usual suspect behind "sync never finishes".
fn caches(core: &Core) -> Value {
    json!({
        "tracks_cached": crate::server::commands::library::cached_tracks_count(&core.metadata_cache),
        "track_index_count": core.metadata_cache.track_count(),
        "covers_cached": core.cover_store.cached_count(),
        "metadata_ready": core.metadata_cache.is_validated(),
        "cover_build_running": core.cover_store.is_building(),
        "reconciling": core.is_reconciling(),
        "tracks_synced_at_unix": core.metadata_cache.tracks_synced_at(),
    })
}

/// Where the update flow stands, plus the persisted bookkeeping behind it -
/// which channel the user follows is often the first thing worth knowing about
/// a beta report.
fn update(core: &Core) -> Value {
    let status = crate::updates::service::status(core);
    json!({
        "status": status,
        "channel": core.config.update_channel,
        "check_enabled": core.config.update_check_enabled,
        "check_interval_hours": core.config.update_check_interval_hours,
    })
}
