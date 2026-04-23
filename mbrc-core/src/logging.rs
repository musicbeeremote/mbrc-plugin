use std::path::Path;

use tracing_appender::rolling;
use tracing_subscriber::fmt;
use tracing_subscriber::prelude::*;
use tracing_subscriber::EnvFilter;

/// Initialize tracing with a daily rolling log file at `{storage_path}/mb_remote/rust_core.log`.
/// Also logs to stderr for debugging when attached to a console.
pub fn init(storage_path: &str) {
    let log_dir = Path::new(storage_path).join("mb_remote");

    // Daily rolling file appender
    let file_appender = rolling::daily(&log_dir, "rust_core.log");
    let (file_writer, _guard) = tracing_appender::non_blocking(file_appender);

    // We intentionally leak the guard so the file writer stays alive for the
    // entire process lifetime. The process is MusicBee itself — when it exits,
    // the OS reclaims everything.
    std::mem::forget(_guard);

    let filter = EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new("info"));

    let subscriber = tracing_subscriber::registry().with(filter).with(
        fmt::layer()
            .with_writer(file_writer)
            .with_ansi(false)
            .with_target(true)
            .with_thread_ids(true),
    );

    // Ignore error if a global subscriber is already set (shouldn't happen)
    let _ = tracing::subscriber::set_global_default(subscriber);
}
