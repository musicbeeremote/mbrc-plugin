//! `report.json`: the environment and core state a bug report needs, assembled
//! in one place.
//!
//! Nothing here is new state - it is the values the settings panel, update flow
//! and caches already hold, gathered so a maintainer never has to ask for them.

use serde_json::{Value, json};
use time::{OffsetDateTime, format_description::well_known::Rfc3339};

use crate::diagnostics::redact;
use crate::ffi::dtos::CaptureEnvEntry;
use crate::state::Core;

/// Assembles the report for a capture that began at `started_unix_ms`.
///
/// Takes `&Core` rather than the global state so it stays testable and the
/// caller keeps control of the lock - none of these reads should happen with
/// the core mutex held.
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
        "connections": connections(core),
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
        "protocol_supported": crate::protocol::version::ADVERTISED_PROTOCOLS,
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

/// What is connected right now. The blocked list below records refusals, but a
/// refusal is only the symptom - the live counts show the accumulation behind
/// it, which a bug reporter has no other way to produce.
fn connections(core: &Core) -> Value {
    let stats = core.registry.stats();
    json!({
        "handshaked": stats.handshaked,
        "subscribers": stats.subscribers,
        "unhandshaked": stats.unhandshaked,
        "slots_by_ip": stats
            .slots_by_ip
            .iter()
            .map(|(ip, count)| json!({ "ip": ip, "count": count }))
            .collect::<Vec<_>>(),
        "oldest_aux_idle_secs": stats.oldest_aux_idle_secs,
        "subscriber_idle_secs": stats.subscriber_idle_secs,
        "evicted_total": stats.evicted_total,
        "rejected_per_ip_total": stats.rejected_per_ip_total,
    })
}

/// Caches health, the usual suspect behind "sync never finishes".
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

/// Where the update flow stands, plus the persisted bookkeeping behind it. The
/// channel the user follows is often the first thing a beta report needs.
fn update(core: &Core) -> Value {
    let status = crate::updates::service::status(core);
    json!({
        "status": status,
        "channel": core.config.update_channel,
        "check_enabled": core.config.update_check_enabled,
        "check_interval_hours": core.config.update_check_interval_hours,
    })
}
