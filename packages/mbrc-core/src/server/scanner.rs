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

/// Runs the Scanner loop until `shutdown` fires.
pub async fn run(core: Arc<Core>, shutdown: Arc<Notify>) {
    let mut interval = tokio::time::interval(Duration::from_secs(SCAN_INTERVAL_SECS));
    // Skip a missed-tick backlog rather than firing catch-up scans.
    interval.set_missed_tick_behavior(MissedTickBehavior::Skip);
    // The init reconcile already built the cache, so swallow the first tick.
    interval.tick().await;

    loop {
        tokio::select! {
            _ = shutdown.notified() => return,
            _ = core.scanner_nudge.notified() => {
                // Debounce: let a burst of per-file nudges settle before scanning.
                tokio::time::sleep(Duration::from_secs(DEBOUNCE_SECS)).await;
                scan_after_a_library_change(&core).await;
            }
            _ = interval.tick() => {
                if core.broadcaster.client_count() > 0 {
                    // Debug-gated, so the syscall is skipped when filtered out.
                    // Should stay flat under a paging sweep: the cache is O(page).
                    tracing::debug!(
                        rss_mib = crate::logging::rss_mib(),
                        tracks = core.metadata_cache.track_count(),
                        clients = core.broadcaster.client_count(),
                        "core memory sample"
                    );
                    scan_periodically(&core).await;
                }
            }
        }
    }
}

/// A delta pass prompted by an explicit change signal.
///
/// A nudge means the library really changed - a file added, its tags edited, or
/// it deleted - so the cover cache is refreshed too. This is the only path by
/// which a runtime artwork edit reaches the grid.
async fn scan_after_a_library_change(core: &Arc<Core>) {
    scan(core, true).await;
}

/// The periodic safety net, for metadata only.
///
/// The cover delta rides the nudge path instead, so an idle tick never pays for
/// the extra album-enumeration FFI.
async fn scan_periodically(core: &Arc<Core>) {
    scan(core, false).await;
}

/// Runs one delta pass on a blocking worker, under the reconcile single-flight
/// guard, so it never races an init or library-switch rebuild.
async fn scan(core: &Arc<Core>, covers: bool) {
    use crate::ffi::types::HostEventType;

    let core = core.clone();
    let _ = tokio::task::spawn_blocking(move || {
        let Some(reconcile) = core.begin_reconcile() else {
            tracing::debug!("scanner: reconcile in progress; skipping delta");
            return;
        };
        // Tells the panel's cache-status line a scan is running, and is paired
        // with the finish event below so the line clears again.
        core.providers
            .emit_event(HostEventType::CacheStatusChanged, &[]);
        commands::library::refresh_library_delta(&core.metadata_cache, core.providers.as_ref());
        if covers {
            super::refresh_covers_delta(&core);
        }
        // Released before the finish event: the panel answers that event by
        // re-reading `is_reconciling`, so the guard must already be gone.
        drop(reconcile);
        core.providers
            .emit_event(HostEventType::CacheStatusChanged, &[]);
    })
    .await;
}
