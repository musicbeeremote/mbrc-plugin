//! The background library Scanner: keeps the ordinal track index and browse
//! caches fresh without depending on catching every MusicBee notification.
//!
//! Two triggers, both funnelling into one single-flight delta pass:
//! - a **nudge** (`core.scanner_nudge`) raised by a `FileAddedToLibrary`
//!   notification, debounced so a big import that fires it per-file collapses to
//!   a scan or two;
//! - a **periodic tick** (~60s) as a safety net for changes no notification
//!   covered, run only while a client is connected so an idle core does no FFI.
//!
//! The scan itself (a full path refetch + a sync-delta + the small-list prewarm)
//! is blocking MusicBee FFI, so it runs on a blocking worker and shares the
//! reconcile single-flight guard, so it never overlaps an init/library-switch
//! rebuild.

use std::sync::Arc;
use std::time::Duration;

use tokio::sync::Notify;
use tokio::time::MissedTickBehavior;

use super::commands;
use crate::state::Core;

/// Safety-net interval between delta passes while clients are connected.
const SCAN_INTERVAL_SECS: u64 = 60;
/// After a nudge, wait this long (draining further nudges) before scanning, so a
/// burst of per-file `FileAddedToLibrary` notifications coalesces into one pass.
const DEBOUNCE_SECS: u64 = 2;

/// Run the Scanner loop until `shutdown` fires.
pub async fn run(core: Arc<Core>, shutdown: Arc<Notify>) {
    let mut interval = tokio::time::interval(Duration::from_secs(SCAN_INTERVAL_SECS));
    // A long blocking scan can miss ticks; skip the backlog instead of firing a
    // burst of catch-up scans right after.
    interval.set_missed_tick_behavior(MissedTickBehavior::Skip);
    // The first tick fires immediately; the init reconcile already built the
    // cache, so swallow it.
    interval.tick().await;

    loop {
        tokio::select! {
            _ = shutdown.notified() => return,
            _ = core.scanner_nudge.notified() => {
                // Debounce: let a burst of per-file nudges settle before scanning.
                tokio::time::sleep(Duration::from_secs(DEBOUNCE_SECS)).await;
                scan(&core).await;
            }
            _ = interval.tick() => {
                if core.broadcaster.client_count() > 0 {
                    // Periodic RSS sample (debug-gated, so the syscall is skipped
                    // when the level filters it out): during a paging sweep of a
                    // huge library this should stay flat, proving the cache is
                    // O(page) - the server-side half of the validation plan.
                    tracing::debug!(
                        rss_mib = crate::logging::rss_mib(),
                        tracks = core.metadata_cache.track_count(),
                        clients = core.broadcaster.client_count(),
                        "core memory sample"
                    );
                    scan(&core).await;
                }
            }
        }
    }
}

/// Run one delta pass on a blocking worker, under the reconcile single-flight
/// guard (so it never races an init/library-switch rebuild).
async fn scan(core: &Arc<Core>) {
    let core = core.clone();
    let _ = tokio::task::spawn_blocking(move || {
        if !core.try_begin_reconcile() {
            tracing::debug!("scanner: reconcile in progress; skipping delta");
            return;
        }
        commands::library::refresh_library_delta(&core.metadata_cache, core.providers.as_ref());
        core.end_reconcile();
    })
    .await;
}
