//! The background library Scanner: turns what MusicBee reports into the
//! library caches and the change log.
//!
//! MusicBee's library notifications are the only source of change while the
//! plugin runs. Each is queued in [`LibraryEvents`] and nudges the Scanner,
//! which waits out a short debounce so a bulk edit is one pass:
//! - a rating names its file, which is filed in the change log and has its
//!   cached tags dropped, without listing the library;
//! - an add, a delete or a tag edit can move the browse index MusicBee orders,
//!   so the pass re-lists the paths and the diff against the log files it.
//!
//! A change MusicBee does not report is not looked for. The reconcile lists the
//! library and asks for edits since the watermark at every start.
//!
//! The tag cache that search and sort read is filled in paced bursts after a
//! reconcile, and after a pass that left tags missing, until it is complete.
//! Nothing runs on a timer. Every pass is blocking MusicBee FFI on a blocking
//! worker, under the reconcile single-flight guard.

use std::collections::BTreeSet;
use std::sync::{Arc, Mutex, MutexGuard};
use std::time::{Duration, Instant};

use tokio::sync::Notify;

use super::commands;
use crate::metadata_cache::{CachedTags, MetadataCache, SortField};
use crate::state::Core;

/// Tracks whose tags are fetched per FFI batch.
const BACKFILL_BATCH: usize = 500;

/// How long one backfill burst may spend filling the tag cache.
///
/// A batch of 500 measures around 200ms, so a burst fills about 5,000 tracks.
const BACKFILL_BUDGET: Duration = Duration::from_secs(2);
/// The rest between backfill bursts, so other MusicBee calls get the API lock.
const BACKFILL_PAUSE: Duration = Duration::from_secs(5);
/// After a nudge, wait this long (draining further nudges) before scanning, so a
/// burst of per-file notifications coalesces into one pass.
const DEBOUNCE_SECS: u64 = 2;

/// The library changes MusicBee reported since the last pass.
#[derive(Default)]
pub struct LibraryEvents(Mutex<Reported>);

/// What a pass has to apply.
#[derive(Debug, Default, PartialEq, Eq)]
pub struct Reported {
    /// The browse index may have moved: a file was added, removed or retagged.
    pub relist: bool,
    /// Files whose tags changed.
    pub tagged: BTreeSet<String>,
    /// Files whose rating changed, which moves nothing in the browse order.
    pub rated: BTreeSet<String>,
}

impl Reported {
    fn is_empty(&self) -> bool {
        !self.relist && self.tagged.is_empty() && self.rated.is_empty()
    }
}

impl LibraryEvents {
    /// A file was added to or removed from the library.
    pub fn membership_changed(&self) {
        self.lock().relist = true;
    }

    /// A file's tags changed. Without a path, only the re-list can say what moved.
    pub fn tags_changed(&self, path: Option<&str>) {
        let mut queued = self.lock();
        queued.relist = true;
        if let Some(path) = path {
            queued.tagged.insert(path.to_string());
        }
    }

    /// A file's rating changed. Without a path there is nothing to file.
    pub fn rating_changed(&self, path: Option<&str>) {
        if let Some(path) = path {
            self.lock().rated.insert(path.to_string());
        }
    }

    /// Everything reported so far, leaving the queue empty.
    pub fn take(&self) -> Reported {
        std::mem::take(&mut *self.lock())
    }

    /// Returns a batch a pass could not apply, merged with whatever arrived since.
    fn put_back(&self, batch: Reported) {
        let mut queued = self.lock();
        queued.relist |= batch.relist;
        queued.tagged.extend(batch.tagged);
        queued.rated.extend(batch.rated);
    }

    fn lock(&self) -> MutexGuard<'_, Reported> {
        self.0.lock().unwrap_or_else(|e| e.into_inner())
    }
}

/// Runs the Scanner loop until `shutdown` fires.
pub async fn run(core: Arc<Core>, shutdown: Arc<Notify>) {
    let mut backfilling = false;
    loop {
        tokio::select! {
            _ = shutdown.notified() => return,
            _ = core.scanner_nudge.notified() => {
                // Debounce: let a burst of per-file nudges settle before scanning.
                tokio::time::sleep(Duration::from_secs(DEBOUNCE_SECS)).await;
                apply_reported(&core).await;
            }
            _ = core.tag_backfill.notified() => backfilling = true,
            _ = tokio::time::sleep(BACKFILL_PAUSE), if backfilling => {
                backfilling = backfill_burst(&core).await;
            }
        }
    }
}

/// Rebuilds whatever is derived from the tags and has gone missing.
///
/// Each is asked about separately: they are built together but can be lost
/// apart, and gating the album years on the sort orders left every album
/// undated with nothing that would rebuild them.
///
/// Call only on a complete cache: a half-built order pages a reader through a
/// fraction of the library and calls it the whole thing.
fn rebuild_derived(cache: &MetadataCache) {
    let orders_missing = cache.sorted_track_count(SortField::Title) != cache.track_count();
    let years_missing = cache.album_years().is_empty();
    if !orders_missing && !years_missing {
        return;
    }
    let indexed = cache.rebuild_sort_orders();
    let dated = cache.rebuild_album_years();
    tracing::info!(indexed, dated, "scanner: sort orders rebuilt");
}

/// Fetches tags for tracks that have none, for up to [`BACKFILL_BUDGET`].
///
/// Browsing fills this cache for the pages someone looked at, which is enough
/// to render them and not enough to search or sort by. This walks the rest,
/// and rebuilds the sort orders once nothing is missing. Returns whether
/// another burst would make progress: only when the budget ran out, so tracks
/// the host cannot describe, or a failing host, do not keep it running.
fn backfill_tags(core: &Arc<Core>) -> bool {
    let cache = &core.metadata_cache;
    let started = Instant::now();
    let mut fetched = 0usize;
    let mut complete = false;
    let mut stuck = false;

    while started.elapsed() < BACKFILL_BUDGET {
        let missing = cache.untagged_paths(BACKFILL_BATCH);
        if missing.is_empty() {
            complete = true;
            break;
        }
        match core.providers.tracks_detailed_for_paths(missing) {
            Ok(tags) => {
                let cached: Vec<CachedTags> = tags.iter().map(CachedTags::from).collect();
                // Nothing back for paths it was asked about: asking again
                // would spin on the same rows.
                if cached.is_empty() {
                    stuck = true;
                    break;
                }
                fetched += cached.len();
                cache.put_track_tags(&cached);
            }
            Err(error) => {
                tracing::debug!(%error, "scanner: tag backfill failed");
                stuck = true;
                break;
            }
        }
    }

    if fetched > 0 {
        // Debug-gated, so the syscall is skipped when filtered out.
        tracing::debug!(
            fetched,
            elapsed_ms = started.elapsed().as_millis() as u64,
            rss_mib = crate::logging::rss_mib(),
            "scanner: tag backfill"
        );
    }
    if complete {
        rebuild_derived(cache);
    }
    !complete && !stuck
}

/// Applies what MusicBee reported, on a blocking worker.
async fn apply_reported(core: &Arc<Core>) {
    let core = core.clone();
    let _ = tokio::task::spawn_blocking(move || apply_reported_now(&core)).await;
}

/// Applies the queued reports under the reconcile guard.
///
/// A batch that meets a running reconcile goes back on the queue and the
/// Scanner is nudged again, so an edit made during a rebuild is not lost.
pub(crate) fn apply_reported_now(core: &Arc<Core>) {
    use crate::ffi::types::HostEventType;

    let reported = core.library_events.take();
    if reported.is_empty() {
        return;
    }
    let Some(reconcile) = core.begin_reconcile() else {
        tracing::debug!("scanner: reconcile in progress; retrying the reported changes");
        core.library_events.put_back(reported);
        core.scanner_nudge.notify_one();
        return;
    };
    // Tells the panel's cache-status line a pass is running, and is paired
    // with the finish event below so the line clears again.
    core.providers
        .emit_event(HostEventType::CacheStatusChanged, &[]);

    let cache = &core.metadata_cache;
    let tagged: Vec<String> = reported.tagged.into_iter().collect();
    let mut edited = tagged.clone();
    edited.extend(reported.rated);
    edited.sort_unstable();
    edited.dedup();
    if reported.relist {
        commands::library::relist_library(cache, core.providers.as_ref(), &edited);
    } else {
        commands::library::file_reported_edits(cache, &edited);
    }
    crate::state::broadcast_library_changed(core);

    if backfill_tags(core) {
        core.tag_backfill.notify_one();
    }
    // A rating moves no artwork, so a pass of ratings alone leaves covers be.
    if reported.relist {
        super::refresh_covers_delta(core, &tagged);
    }
    // Released before the finish event: the panel answers that event by
    // re-reading `is_reconciling`, so the guard must already be gone.
    drop(reconcile);
    core.providers
        .emit_event(HostEventType::CacheStatusChanged, &[]);
}

/// One backfill burst on a blocking worker, under the reconcile guard.
///
/// Returns whether to come back for another. A running reconcile holds the
/// guard, so the burst waits for it rather than giving up.
async fn backfill_burst(core: &Arc<Core>) -> bool {
    let core = core.clone();
    tokio::task::spawn_blocking(move || match core.begin_reconcile() {
        Some(_reconcile) => backfill_tags(&core),
        None => true,
    })
    .await
    .unwrap_or(false)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::Config;

    #[test]
    fn reports_collapse_per_file_and_an_unnamed_tag_edit_asks_for_a_relist() {
        let events = LibraryEvents::default();
        events.tags_changed(Some("/a.mp3"));
        events.tags_changed(Some("/a.mp3"));
        events.rating_changed(Some("/b.mp3"));
        let batch = events.take();
        assert!(batch.relist);
        assert_eq!(batch.tagged.len(), 1);
        assert_eq!(batch.rated.len(), 1);
        assert_eq!(events.take(), Reported::default());

        events.rating_changed(Some("/b.mp3"));
        assert!(!events.take().relist, "a rating moves nothing in the order");

        events.tags_changed(None);
        assert!(events.take().relist);
    }

    fn core_with(m: Arc<crate::providers::MockProviders>, name: &str) -> Arc<Core> {
        let dir = std::env::temp_dir().join(format!("mbrc-scanner-{name}"));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let config = Config {
            storage_path: dir.to_string_lossy().into_owned(),
            ..Config::for_test(0)
        };
        let core = Arc::new(Core::new(m, config));
        core.metadata_cache.reconcile(&[], 1);
        let library = vec!["/a.mp3".to_string(), "/b.mp3".to_string()];
        core.metadata_cache.replace_track_index(&library);
        core.metadata_cache.record_changes(Some(&library), &[]);
        core
    }

    #[test]
    fn a_reported_rating_is_filed_without_listing_the_library_or_its_covers() {
        let m = Arc::new(crate::providers::MockProviders::default());
        let core = core_with(m.clone(), "rating");
        let cursor = core.metadata_cache.changes().head().unwrap();

        core.library_events.rating_changed(Some("/b.mp3"));
        apply_reported_now(&core);

        let calls = m.recorded();
        assert!(!calls.iter().any(|c| c == "track_paths"), "{calls:?}");
        assert!(!calls.iter().any(|c| c == "album_identifiers"), "{calls:?}");
        let page = core
            .metadata_cache
            .changes()
            .read(crate::library_changes::ReadFrom::Since(&cursor), 10);
        let Some(crate::library_changes::ChangePage::Changes { changes, .. }) = page else {
            panic!("expected a page");
        };
        let filed: Vec<&str> = changes.iter().map(|c| c.src.as_str()).collect();
        assert_eq!(filed, vec!["/b.mp3"]);
    }

    /// A retagged artist moves the track in MusicBee's browse order.
    #[test]
    fn a_reported_tag_edit_relists_the_library() {
        let m = Arc::new(crate::providers::MockProviders {
            track_paths: vec!["/b.mp3".into(), "/a.mp3".into()],
            ..Default::default()
        });
        let core = core_with(m.clone(), "tags");

        core.library_events.tags_changed(Some("/a.mp3"));
        apply_reported_now(&core);

        assert_eq!(
            core.metadata_cache.track_page_paths(0, 1),
            vec!["/b.mp3".to_string()]
        );
        assert_eq!(core.metadata_cache.changes().head().unwrap().generation, 2);
    }

    #[test]
    fn a_reported_add_relists_the_library() {
        let m = Arc::new(crate::providers::MockProviders {
            track_paths: vec!["/a.mp3".into(), "/b.mp3".into(), "/c.mp3".into()],
            ..Default::default()
        });
        let core = core_with(m.clone(), "add");

        core.library_events.membership_changed();
        apply_reported_now(&core);

        assert!(m.recorded().iter().any(|c| c == "track_paths"));
        assert_eq!(core.metadata_cache.changes().head().unwrap().generation, 2);
        assert_eq!(core.metadata_cache.track_count(), 3);
    }

    fn tags(src: &str) -> crate::protocol::messages::TrackTags {
        crate::protocol::messages::TrackTags {
            src: src.into(),
            title: src.into(),
            ..Default::default()
        }
    }

    #[test]
    fn a_backfill_that_completes_the_cache_stops_and_builds_the_orders() {
        let m = Arc::new(crate::providers::MockProviders {
            tracks_detailed: vec![tags("/a.mp3"), tags("/b.mp3")],
            ..Default::default()
        });
        let core = core_with(m, "backfill-done");

        assert!(!backfill_tags(&core), "nothing left to fetch");
        assert!(core.metadata_cache.untagged_paths(1).is_empty());
        assert_eq!(core.metadata_cache.sorted_track_count(SortField::Title), 2);
    }

    /// A track the host cannot describe stays untagged; the bursts must not
    /// keep coming back for it.
    #[test]
    fn a_backfill_that_fetches_nothing_stops() {
        let m = Arc::new(crate::providers::MockProviders {
            tracks_detailed: vec![tags("/a.mp3")],
            ..Default::default()
        });
        let core = core_with(m, "backfill-stuck");

        assert!(!backfill_tags(&core));
        assert_eq!(
            core.metadata_cache.untagged_paths(10),
            vec!["/b.mp3".to_string()]
        );
    }

    #[test]
    fn a_batch_put_back_merges_with_what_arrived_since() {
        let events = LibraryEvents::default();
        events.rating_changed(Some("/a.mp3"));
        let batch = events.take();
        events.membership_changed();
        events.put_back(batch);
        let merged = events.take();
        assert!(merged.relist);
        assert!(merged.rated.contains("/a.mp3"));
    }
}
