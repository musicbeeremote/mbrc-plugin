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

use std::collections::{BTreeSet, HashSet};
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
/// The rest between backfill bursts, so reading tags does not keep MusicBee busy.
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
    /// The browse index may have moved: a file was added or removed, or an
    /// edit named no file.
    pub relist: bool,
    /// An edit named no file, so the change log cannot say what moved.
    pub unnamed_edit: bool,
    /// Files whose tags changed. Only those in the music library move its index.
    pub tagged: BTreeSet<String>,
    /// Files whose rating changed, which moves nothing in the browse order.
    pub rated: BTreeSet<String>,
    /// Files added to or removed from the library, of any category.
    pub moved: BTreeSet<String>,
}

impl Reported {
    fn is_empty(&self) -> bool {
        !self.relist && self.tagged.is_empty() && self.rated.is_empty()
    }
}

impl LibraryEvents {
    /// A file was added to or removed from the library.
    pub fn membership_changed(&self, path: Option<&str>) {
        let mut queued = self.lock();
        queued.relist = true;
        if let Some(path) = path {
            queued.moved.insert(path.to_string());
        }
    }

    /// A file's tags changed. Without a path, only the re-list can say what moved.
    pub fn tags_changed(&self, path: Option<&str>) {
        let mut queued = self.lock();
        match path {
            Some(path) => {
                queued.tagged.insert(path.to_string());
            }
            None => {
                queued.relist = true;
                queued.unnamed_edit = true;
            }
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
        queued.unnamed_edit |= batch.unnamed_edit;
        queued.tagged.extend(batch.tagged);
        queued.rated.extend(batch.rated);
        queued.moved.extend(batch.moved);
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
    let orders_missing = SortField::ALL
        .into_iter()
        .any(|field| cache.sorted_track_count(field) != cache.track_count());
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
    let split = cache
        .media()
        .holds_listings()
        .then(|| MediaSplit::before_pass(cache, &reported));
    let tagged: Vec<String> = reported.tagged.into_iter().collect();
    let relist = reported.relist || !tagged.is_empty();
    let mut edited = tagged.clone();
    edited.extend(reported.rated);
    edited.sort_unstable();
    edited.dedup();
    let filed = if relist {
        commands::library::relist_library(cache, core.providers.as_ref(), &edited)
    } else {
        commands::library::file_reported_edits(cache, &edited)
    };
    if filed.is_some() || (reported.unnamed_edit && cache.is_validated()) {
        crate::state::broadcast_library_changed(core);
    }
    let library_tagged = cache.in_library(&tagged);
    super::commands_v6::tags::refresh_rows(core.providers.as_ref(), Some(cache), &library_tagged);
    announce_playing_edit(core, &tagged);
    if let Some(split) = split {
        cache.media().files_changed(&split.outside(cache));
    }

    if backfill_tags(core) {
        core.tag_backfill.notify_one();
    }
    // A rating moves no artwork, so a pass of ratings alone leaves covers be.
    if relist {
        super::refresh_covers_delta(core, &library_tagged);
    }
    // Released before the finish event: the panel answers that event by
    // re-reading `is_reconciling`, so the guard must already be gone.
    drop(reconcile);
    core.providers
        .emit_event(HostEventType::CacheStatusChanged, &[]);
}

/// The reported files that belong to the media categories (#145), told apart
/// by the music index as it stood before the pass.
///
/// The music pass still sees every file: a tag edit is how a file can enter
/// the music library, and one it does not hold is never filed. Until the
/// library is first listed nothing can be told apart, so every file also goes
/// to the media categories, where a miss costs one re-list. Taken only once
/// some category is listed: before that no client holds media to refresh.
struct MediaSplit {
    validated: bool,
    /// Edits to files the music library did not hold.
    edited: Vec<String>,
    /// Adds and removals the music library did not hold before the pass.
    moved: Vec<String>,
}

impl MediaSplit {
    fn before_pass(cache: &crate::metadata_cache::MetadataCache, reported: &Reported) -> Self {
        let edited = reported.tagged.iter().chain(&reported.rated);
        let validated = cache.is_validated();
        if !validated {
            return Self {
                validated,
                edited: edited.cloned().collect(),
                moved: reported.moved.iter().cloned().collect(),
            };
        }
        let named: Vec<String> = edited.clone().chain(&reported.moved).cloned().collect();
        let in_music: HashSet<String> = cache.in_library(&named).into_iter().collect();
        let outside = |p: &&String| !in_music.contains(*p);
        Self {
            validated,
            edited: edited.filter(outside).cloned().collect(),
            moved: reported.moved.iter().filter(outside).cloned().collect(),
        }
    }

    /// The files for the media categories, leaving out adds the pass just
    /// filed in the music library.
    fn outside(mut self, cache: &crate::metadata_cache::MetadataCache) -> Vec<String> {
        if self.validated {
            let in_music: HashSet<String> = cache.in_library(&self.moved).into_iter().collect();
            self.moved.retain(|p| !in_music.contains(p));
        }
        self.edited.extend(self.moved);
        self.edited
    }
}

/// Re-reads the playing track and tells V6 clients, when it is among `tagged`.
///
/// Done here, after the debounce, rather than when MusicBee reports the edit:
/// it reports an edit to the playing file as `TagsChanging` before applying
/// it, so a read then sends clients the values being replaced.
fn announce_playing_edit(core: &Core, tagged: &[String]) {
    let playing = core.now_playing.track_info().path;
    if playing.is_empty() || !tagged.contains(&playing) {
        return;
    }
    core.now_playing.refresh_track_bundle();
    core.v6_broadcaster.broadcast(&[mbrc_wire::v6::event(
        "now_playing_tags_changed",
        serde_json::json!({ "path": playing }),
    )]);
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

    #[test]
    fn reports_collapse_per_file_and_only_an_unnamed_tag_edit_asks_for_a_relist() {
        let events = LibraryEvents::default();
        events.tags_changed(Some("/a.mp3"));
        events.tags_changed(Some("/a.mp3"));
        events.rating_changed(Some("/b.mp3"));
        let batch = events.take();
        assert!(!batch.relist, "the pass decides from where the file is");
        assert_eq!(batch.tagged.len(), 1);
        assert_eq!(batch.rated.len(), 1);
        assert_eq!(events.take(), Reported::default());

        events.rating_changed(Some("/b.mp3"));
        assert!(!events.take().relist, "a rating moves nothing in the order");

        events.tags_changed(None);
        let batch = events.take();
        assert!(batch.relist && batch.unnamed_edit);
    }

    fn core_with(m: Arc<crate::providers::MockProviders>, name: &str) -> Arc<Core> {
        let core = crate::state::test_core(m, &format!("scanner-{name}"));
        core.metadata_cache.reconcile(&[], 1);
        let library = vec!["/a.mp3".to_string(), "/b.mp3".to_string()];
        core.metadata_cache.replace_track_index(&library);
        core.metadata_cache.record_changes(Some(&library), &[]);
        core
    }

    /// An edit to the playing file, from a client or in MusicBee's own window,
    /// reaches every client showing its tags once the pass has read it, and
    /// not before: MusicBee reports the edit before applying it.
    #[test]
    fn a_tag_edit_to_the_playing_file_is_announced_after_the_pass() {
        let m = Arc::new(crate::providers::MockProviders {
            track_info: crate::protocol::messages::TrackInfo {
                path: "/a.mp3".into(),
                ..Default::default()
            },
            ..Default::default()
        });
        let core = core_with(m.clone(), "playing-edit");
        let (tx, mut rx) = tokio::sync::mpsc::unbounded_channel::<String>();
        core.v6_broadcaster.register(1, tx);
        let announced = |rx: &mut tokio::sync::mpsc::UnboundedReceiver<String>| {
            std::iter::from_fn(|| rx.try_recv().ok())
                .filter(|frame| frame.contains("now_playing_tags_changed"))
                .count()
        };

        crate::state::dispatch_notification(
            &core,
            crate::ffi::types::NotificationType::TagsChanged,
            Some("/a.mp3"),
        );
        assert_eq!(
            announced(&mut rx),
            0,
            "announced before MusicBee applied it"
        );

        apply_reported_now(&core);
        assert_eq!(announced(&mut rx), 1);

        core.library_events.tags_changed(Some("/b.mp3"));
        apply_reported_now(&core);
        assert_eq!(
            announced(&mut rx),
            0,
            "another file's edit is not the playing track's"
        );
    }

    /// Lists the audiobooks (empty) as a client reading them would.
    fn list_media(core: &Core) {
        core.metadata_cache
            .media()
            .paths(
                core.providers.as_ref(),
                crate::media::MediaSource::Audiobooks,
            )
            .unwrap();
    }

    /// An audiobook, video or inbox file is not the music library's: its edit
    /// goes to the media listings, files nothing in the music library, and is
    /// still announced when it is the playing track.
    #[test]
    fn an_edit_outside_the_music_library_goes_to_media_and_is_still_announced() {
        let m = Arc::new(crate::providers::MockProviders {
            track_info: crate::protocol::messages::TrackInfo {
                path: "/books/dune.mp3".into(),
                ..Default::default()
            },
            track_paths: vec!["/a.mp3".into(), "/b.mp3".into()],
            ..Default::default()
        });
        let core = core_with(m.clone(), "media-edit");
        list_media(&core);
        let (tx, mut rx) = tokio::sync::mpsc::unbounded_channel::<String>();
        core.v6_broadcaster.register(1, tx);

        core.library_events.tags_changed(Some("/books/dune.mp3"));
        apply_reported_now(&core);

        let frames: Vec<String> = std::iter::from_fn(|| rx.try_recv().ok()).collect();
        assert!(
            frames
                .iter()
                .any(|f| f.contains("now_playing_tags_changed")),
            "{frames:?}"
        );
        assert!(
            !frames.iter().any(|f| f.contains("library_changed")),
            "{frames:?}"
        );
        assert!(!core.metadata_cache.media().take_pending().relist.is_empty());
    }

    /// A file added to the music library is the music library's; one that is
    /// in it neither before nor after the pass belongs to a media category.
    #[test]
    fn only_adds_and_removals_outside_the_music_library_reach_media() {
        let m = Arc::new(crate::providers::MockProviders {
            track_paths: vec!["/a.mp3".into(), "/b.mp3".into(), "/c.mp3".into()],
            ..Default::default()
        });
        let core = core_with(m, "media-moved");
        list_media(&core);
        let (tx, mut rx) = tokio::sync::mpsc::unbounded_channel::<String>();
        core.v6_broadcaster.register(1, tx);

        core.library_events.membership_changed(Some("/c.mp3"));
        apply_reported_now(&core);
        assert_eq!(
            core.metadata_cache.media().take_pending(),
            crate::media::Pending::default()
        );
        assert_eq!(
            crate::state::drain_events(&mut rx, "library_changed").len(),
            1
        );

        core.library_events
            .membership_changed(Some("/books/gone.mp3"));
        apply_reported_now(&core);
        assert!(!core.metadata_cache.media().take_pending().relist.is_empty());
    }

    /// Before the music library is listed nothing tells the two apart, so an
    /// edit reaches media too rather than neither.
    #[test]
    fn before_the_first_listing_an_edit_reaches_media_too() {
        let core = crate::state::test_core(
            Arc::new(crate::providers::MockProviders::default()),
            "scanner-unlisted",
        );

        core.library_events.tags_changed(Some("/books/dune.mp3"));
        let reported = core.library_events.take();
        let split = MediaSplit::before_pass(&core.metadata_cache, &reported);

        assert_eq!(split.outside(&core.metadata_cache), ["/books/dune.mp3"]);
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

        core.library_events.membership_changed(None);
        apply_reported_now(&core);

        assert!(m.recorded().iter().any(|c| c == "track_paths"));
        assert_eq!(core.metadata_cache.changes().head().unwrap().generation, 2);
        assert_eq!(core.metadata_cache.track_count(), 3);
    }

    fn library_changed_frames(rx: &mut tokio::sync::mpsc::UnboundedReceiver<String>) -> usize {
        std::iter::from_fn(|| rx.try_recv().ok())
            .filter(|frame| frame.contains("\"library_changed\""))
            .count()
    }

    /// An audiobook, video or Inbox file is not in the music library, so its
    /// edit moves nothing clients browse.
    #[test]
    fn a_tag_edit_outside_the_music_library_tells_no_one() {
        let m = Arc::new(crate::providers::MockProviders {
            track_paths: vec!["/a.mp3".into(), "/b.mp3".into()],
            ..Default::default()
        });
        let core = core_with(m.clone(), "outside-edit");
        let (tx, mut rx) = tokio::sync::mpsc::unbounded_channel::<String>();
        core.v6_broadcaster.register(1, tx);

        core.library_events
            .tags_changed(Some("/books/chapter-01.mp3"));
        apply_reported_now(&core);

        assert_eq!(core.metadata_cache.changes().head().unwrap().generation, 1);
        assert_eq!(library_changed_frames(&mut rx), 0);

        core.library_events.tags_changed(Some("/a.mp3"));
        apply_reported_now(&core);
        assert_eq!(core.metadata_cache.changes().head().unwrap().generation, 2);
        assert_eq!(library_changed_frames(&mut rx), 1);
    }

    /// A file can enter the music library with only a tag edit to show for it,
    /// as when it moves out of the Inbox.
    #[test]
    fn a_tag_edit_to_a_file_new_to_the_music_library_files_it() {
        let m = Arc::new(crate::providers::MockProviders {
            track_paths: vec!["/a.mp3".into(), "/b.mp3".into(), "/c.mp3".into()],
            ..Default::default()
        });
        let core = core_with(m.clone(), "tagged-into-library");
        let (tx, mut rx) = tokio::sync::mpsc::unbounded_channel::<String>();
        core.v6_broadcaster.register(1, tx);

        core.library_events.tags_changed(Some("/c.mp3"));
        apply_reported_now(&core);

        assert_eq!(core.metadata_cache.track_count(), 3);
        assert_eq!(core.metadata_cache.changes().head().unwrap().generation, 2);
        assert_eq!(library_changed_frames(&mut rx), 1);
    }

    /// An edit that names no file files nothing, yet something clients show moved.
    #[test]
    fn an_unnamed_tag_edit_tells_clients_to_refresh() {
        let m = Arc::new(crate::providers::MockProviders {
            track_paths: vec!["/a.mp3".into(), "/b.mp3".into()],
            ..Default::default()
        });
        let core = core_with(m.clone(), "unnamed-edit");
        let (tx, mut rx) = tokio::sync::mpsc::unbounded_channel::<String>();
        core.v6_broadcaster.register(1, tx);

        core.library_events.tags_changed(None);
        apply_reported_now(&core);

        assert_eq!(core.metadata_cache.changes().head().unwrap().generation, 1);
        assert_eq!(library_changed_frames(&mut rx), 1);
    }

    /// MusicBee announces an audiobook add like a music one; the listing is
    /// what shows the music library did not move.
    #[test]
    fn an_add_that_leaves_the_music_library_unchanged_tells_no_one() {
        let m = Arc::new(crate::providers::MockProviders {
            track_paths: vec!["/a.mp3".into(), "/b.mp3".into()],
            ..Default::default()
        });
        let core = core_with(m.clone(), "outside-add");
        let (tx, mut rx) = tokio::sync::mpsc::unbounded_channel::<String>();
        core.v6_broadcaster.register(1, tx);

        core.library_events.membership_changed(None);
        apply_reported_now(&core);

        assert!(m.recorded().iter().any(|c| c == "track_paths"));
        assert_eq!(core.metadata_cache.changes().head().unwrap().generation, 1);
        assert_eq!(library_changed_frames(&mut rx), 0);
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
        events.membership_changed(None);
        events.put_back(batch);
        let merged = events.take();
        assert!(merged.relist);
        assert!(merged.rated.contains("/a.mp3"));
    }
}
