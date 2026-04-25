use std::path::Path;
use std::sync::OnceLock;

use tracing_appender::rolling;
use tracing_subscriber::fmt;
use tracing_subscriber::prelude::*;
use tracing_subscriber::reload;
use tracing_subscriber::EnvFilter;
use tracing_subscriber::Registry;

/// Reload handle for the active `EnvFilter`. Stashed at init time so
/// the FFI `mbrc_set_log_level` entry can swap the filter at runtime
/// (used by the C#-side debug-logging toggle in the settings UI).
static FILTER_RELOAD: OnceLock<reload::Handle<EnvFilter, Registry>> = OnceLock::new();

/// Initialize tracing with a daily rolling log file at `{storage_path}/mb_remote/mbrc.log`.
///
/// `mbrc.log` is the unified plugin log — both Rust events and C#
/// events forwarded through `mbrc_log` land here, replacing the old
/// split between NLog's `mbrc.log` and the Rust-specific
/// `rust_core.log`.
pub fn init(storage_path: &str) {
    let log_dir = Path::new(storage_path).join("mb_remote");

    let file_appender = rolling::daily(&log_dir, "mbrc.log");
    let (file_writer, _guard) = tracing_appender::non_blocking(file_appender);

    // We intentionally leak the guard so the file writer stays alive for the
    // entire process lifetime. The process is MusicBee itself — when it exits,
    // the OS reclaims everything.
    std::mem::forget(_guard);

    let filter = EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new("info"));
    let (filter_layer, reload_handle) = reload::Layer::new(filter);

    let subscriber = tracing_subscriber::registry().with(filter_layer).with(
        fmt::layer()
            .with_writer(file_writer)
            .with_ansi(false)
            .with_target(true)
            .with_thread_ids(true),
    );

    // Ignore error if a global subscriber is already set (shouldn't happen).
    if tracing::subscriber::set_global_default(subscriber).is_ok() {
        let _ = FILTER_RELOAD.set(reload_handle);
    }
}

/// Swap the active log-level filter. `directive` is anything `EnvFilter`
/// accepts (e.g. `"info"`, `"debug"`, `"mbrc_core=trace,info"`). Returns
/// `false` if logging hasn't been initialised yet, the directive failed
/// to parse, or the underlying reload handle has dropped.
pub fn try_set_filter(directive: &str) -> bool {
    let Some(handle) = FILTER_RELOAD.get() else {
        return false;
    };
    let Ok(filter) = directive.parse::<EnvFilter>() else {
        return false;
    };
    handle.reload(filter).is_ok()
}