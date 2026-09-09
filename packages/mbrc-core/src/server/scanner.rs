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
use std::time::{Duration, Instant};

use tokio::sync::Notify;
use tokio::time::MissedTickBehavior;

use super::commands;
use crate::metadata_cache::{CachedTags, MetadataCache, SortField};
use crate::state::Core;

/// Safety-net interval between delta passes while clients are connected.
const SCAN_INTERVAL_SECS: u64 = 60;

/// Tracks whose tags are fetched per FFI batch.
const BACKFILL_BATCH: usize = 500;

/// How long one pass may spend filling the tag cache.
///
/// A batch of 500 measures around 200ms, so one batch per pass left a library of
/// 15,000 tracks needing half an hour of passes to answer a search or a sort -
/// six seconds of work spread over thirty minutes of waiting. Batches now run
/// back to back until this budget is spent, which fills that library in a few
/// passes while keeping each one a short burst rather than a blocking sweep.
const BACKFILL_BUDGET: Duration = Duration::from_secs(2);
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
                if subscribers(&core) > 0 {
                    // Debug-gated, so the syscall is skipped when filtered out.
                    // Should stay flat under a paging sweep: the cache is O(page).
                    tracing::debug!(
                        rss_mib = crate::logging::rss_mib(),
                        tracks = core.metadata_cache.track_count(),
                        clients = subscribers(&core),
                        "core memory sample"
                    );
                    scan_periodically(&core).await;
                }
            }
        }
    }
}

/// Clients subscribed to broadcasts, on either protocol.
///
/// Both, because a browser registers with the V6 broadcaster alone: counting
/// only the legacy one made a session with nothing but web clients look idle,
/// and the periodic pass - the safety net for changes no notification covered,
/// and the tag backfill that sorting and search are built on - never ran.
fn subscribers(core: &Arc<Core>) -> usize {
    core.broadcaster.client_count() + core.v6_broadcaster.client_count()
}

/// Rebuilds whatever is derived from the tags and has gone missing.
///
/// Each is asked about separately: they are built together but can be lost
/// apart, and gating the album years on the sort orders left every album
/// undated with nothing that would rebuild them.
///
/// Only from a complete cache: a half-built order pages a reader through a
/// fraction of the library and calls it the whole thing.
fn rebuild_derived(cache: &MetadataCache) {
    if !cache.untagged_paths(1).is_empty() {
        return;
    }
    let orders_missing = cache.sorted_track_count(SortField::Title) != cache.track_count();
    let years_missing = cache.album_years().is_empty();
    if !orders_missing && !years_missing {
        return;
    }
    let indexed = cache.rebuild_sort_orders();
    let dated = cache.rebuild_album_years();
    tracing::info!(indexed, dated, "scanner: sort orders rebuilt");
}

/// Fetches tags for a batch of tracks that have none.
///
/// Browsing fills this cache for the pages someone looked at, which is enough
/// to render them and not enough to search or sort by. This walks the rest.
fn backfill_tags(core: &Arc<Core>) {
    let cache = &core.metadata_cache;
    let started = Instant::now();
    let mut fetched = 0usize;

    while started.elapsed() < BACKFILL_BUDGET {
        let missing = cache.untagged_paths(BACKFILL_BATCH);
        if missing.is_empty() {
            break;
        }
        match core.providers.tracks_detailed_for_paths(missing) {
            Ok(tags) => {
                let cached: Vec<CachedTags> = tags.iter().map(CachedTags::from).collect();
                // Nothing back for paths it was asked about: asking again
                // would spin on the same rows for the whole budget.
                if cached.is_empty() {
                    break;
                }
                fetched += cached.len();
                cache.put_track_tags(&cached);
            }
            Err(error) => {
                tracing::debug!(%error, "scanner: tag backfill failed");
                break;
            }
        }
    }

    if fetched > 0 {
        tracing::debug!(
            fetched,
            untagged = cache.untagged_paths(1).len(),
            elapsed_ms = started.elapsed().as_millis() as u64,
            "scanner: tag backfill"
        );
    }

    rebuild_derived(cache);
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
        backfill_tags(&core);
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

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::Config;
    use crate::providers::NullProviders;
    use tokio::sync::mpsc;

    /// A browser registers with the V6 broadcaster alone. Counting only the
    /// legacy one made a web-only session look idle, so the periodic pass - and
    /// with it the tag backfill sorting and search are built on - never ran.
    #[test]
    fn a_web_only_session_counts_as_connected() {
        let core = Arc::new(Core::new(Arc::new(NullProviders), Config::for_test(0)));
        assert_eq!(subscribers(&core), 0);

        let (tx, _rx) = mpsc::unbounded_channel();
        core.v6_broadcaster.register(1, tx);
        assert_eq!(subscribers(&core), 1, "a V6 subscriber is a client");

        let (tx4, _rx4) = mpsc::unbounded_channel();
        core.broadcaster.register(2, tx4);
        assert_eq!(subscribers(&core), 2, "both protocols count");
    }
}
