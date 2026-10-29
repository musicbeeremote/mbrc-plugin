//! The media task: keeps the non-music categories current and announces
//! `media_changed`.
//!
//! It wakes for a change the notifications queued in [`crate::media::Pending`],
//! and on a slow timer while a V6 client is connected, which is the only way
//! to notice what MusicBee does not announce. A re-list waits out
//! [`crate::media::RELIST_EVERY`] while the task keeps answering: a change to a
//! file a listing already holds is announced at once, never held behind one.

use std::collections::BTreeSet;
use std::sync::Arc;
use std::time::Duration;

use tokio::sync::Notify;

use crate::media::{MediaSource, Pending, album_cover_key, album_cover_prefix};
use crate::metadata_cache::album_key;
use crate::state::Core;

/// How often the categories are re-listed while a V6 client is connected.
const TIMER: Duration = Duration::from_secs(300);

/// Lets a burst of notifications settle into one pass.
const DEBOUNCE: Duration = Duration::from_secs(2);

/// Runs the media task until `shutdown` fires.
pub async fn run(core: Arc<Core>, shutdown: Arc<Notify>) {
    run_every(core, shutdown, TIMER, DEBOUNCE).await;
}

async fn run_every(core: Arc<Core>, shutdown: Arc<Notify>, every: Duration, debounce: Duration) {
    let mut timer = tokio::time::interval(every);
    timer.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Skip);
    timer.tick().await;
    let media = core.metadata_cache.media();
    let mut deferred: BTreeSet<MediaSource> = BTreeSet::new();
    loop {
        let due = media.until_relist().unwrap_or_default();
        let timed = tokio::select! {
            _ = shutdown.notified() => return,
            _ = media.nudge.notified() => {
                tokio::time::sleep(debounce).await;
                false
            }
            _ = timer.tick() => true,
            _ = tokio::time::sleep(due), if !deferred.is_empty() => false,
        };
        if timed && core.v6_broadcaster.client_count() > 0 {
            deferred.extend(MediaSource::ALL);
        }
        let mut pending = media.take_pending();
        deferred.append(&mut pending.relist);
        if media.until_relist().is_none() {
            pending.relist = std::mem::take(&mut deferred);
        }
        if pending == Pending::default() {
            continue;
        }
        let worker = core.clone();
        let changed = tokio::task::spawn_blocking(move || check(&worker, pending))
            .await
            .unwrap_or_default();
        broadcast_media_changed(&core, &changed);
    }
}

/// The categories that changed: those already known to, plus what a re-list
/// finds.
///
/// Their tags are read again here, off any client's request, and the covers
/// of albums they no longer have are dropped.
fn check(core: &Core, pending: Pending) -> BTreeSet<MediaSource> {
    let media = core.metadata_cache.media();
    let p = core.providers.as_ref();
    let mut changed = pending.changed;
    if !pending.relist.is_empty() {
        changed.extend(media.relist(p, pending.relist));
    }
    for source in &changed {
        match media.tags(p, *source) {
            Ok(tags) => {
                let kept = tags
                    .raw
                    .iter()
                    .filter(|t| !t.album.is_empty())
                    .map(|t| {
                        album_cover_key(*source, &album_key(&t.album_artist, &t.artist, &t.album))
                    })
                    .collect();
                core.cover_store
                    .retain_items(&album_cover_prefix(*source), &kept);
            }
            Err(error) => {
                tracing::debug!(source = source.as_str(), %error, "media: tag read failed");
            }
        }
    }
    changed
}

/// Tells V6 subscribers which categories to read again, one event each.
fn broadcast_media_changed(core: &Core, sources: &BTreeSet<MediaSource>) {
    let frames: Vec<String> = sources
        .iter()
        .map(|s| mbrc_wire::v6::event("media_changed", serde_json::json!({ "source": s.as_str() })))
        .collect();
    if !frames.is_empty() {
        core.v6_broadcaster.broadcast(&frames);
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::ffi::types::LibrarySource;
    use crate::providers::MockProviders;

    const SHORT: Duration = Duration::from_millis(20);

    fn core_with(m: Arc<MockProviders>, name: &str) -> Arc<Core> {
        crate::state::test_core(m, &format!("media-{name}"))
    }

    fn listing(m: &MockProviders, files: &[&str]) {
        m.media_paths.lock().unwrap().insert(
            LibrarySource::Audiobooks as i32,
            files.iter().map(|f| (*f).to_string()).collect(),
        );
    }

    fn media_events(rx: &mut tokio::sync::mpsc::UnboundedReceiver<String>) -> Vec<String> {
        crate::state::drain_events(rx, "media_changed")
    }

    #[tokio::test(flavor = "multi_thread")]
    async fn a_change_to_a_listed_file_is_announced_once_for_its_source() {
        let m = Arc::new(MockProviders::default());
        listing(&m, &["/b/1.mp3"]);
        let core = core_with(m.clone(), "announce");
        let (tx, mut rx) = tokio::sync::mpsc::unbounded_channel::<String>();
        core.v6_broadcaster.register(1, tx);
        core.metadata_cache
            .media()
            .paths(m.as_ref(), MediaSource::Audiobooks)
            .unwrap();
        let task = tokio::spawn(run_every(
            core.clone(),
            Arc::new(Notify::new()),
            TIMER,
            SHORT,
        ));

        let paths = ["/b/1.mp3".to_string()];
        core.metadata_cache.media().files_changed(&paths);
        core.metadata_cache.media().files_changed(&paths);
        tokio::time::sleep(SHORT * 10).await;
        task.abort();

        let events = media_events(&mut rx);
        assert_eq!(events.len(), 1, "{events:?}");
        assert!(events[0].contains("\"audiobooks\""), "{events:?}");
    }

    #[tokio::test(flavor = "multi_thread")]
    async fn a_change_to_a_listed_file_is_not_held_behind_a_waiting_relist() {
        let m = Arc::new(MockProviders::default());
        listing(&m, &["/b/1.mp3"]);
        let core = core_with(m.clone(), "not-held");
        let (tx, mut rx) = tokio::sync::mpsc::unbounded_channel::<String>();
        core.v6_broadcaster.register(1, tx);
        let media = core.metadata_cache.media();
        media.paths(m.as_ref(), MediaSource::Audiobooks).unwrap();
        media.relist(m.as_ref(), MediaSource::ALL);
        let task = tokio::spawn(run_every(
            core.clone(),
            Arc::new(Notify::new()),
            TIMER,
            SHORT,
        ));

        media.request_relist(&MediaSource::ALL);
        media.files_changed(&["/b/1.mp3".to_string()]);
        tokio::time::sleep(SHORT * 10).await;
        task.abort();

        assert_eq!(
            media_events(&mut rx).len(),
            1,
            "announced before the budget allows a re-list"
        );
        let listed = m
            .recorded()
            .iter()
            .filter(|c| c.starts_with("media_paths"))
            .count();
        assert_eq!(listed, 4, "the re-list itself still waits");
    }

    /// Pruning keeps exactly the keys the ops write: a mismatch would drop
    /// every cover a source has.
    #[test]
    fn a_change_drops_only_the_covers_of_albums_its_source_no_longer_has() {
        let m = MockProviders {
            tracks_detailed: vec![crate::protocol::messages::TrackTags {
                src: "/b/1.mp3".into(),
                album: "Dune".into(),
                album_artist: "Herbert".into(),
                ..Default::default()
            }],
            ..Default::default()
        };
        listing(&m, &["/b/1.mp3"]);
        let core = core_with(Arc::new(m), "prune");
        let book = |album: &str| {
            album_cover_key(
                MediaSource::Audiobooks,
                &album_key("Herbert", "Herbert", album),
            )
        };
        let video = album_cover_key(MediaSource::Videos, &album_key("X", "X", "Clip"));
        for key in [book("Dune"), book("Emma"), video.clone()] {
            core.cover_store.cache_item_cover(&key, None, 0).unwrap();
        }

        check(
            &core,
            Pending {
                changed: [MediaSource::Audiobooks].into(),
                ..Pending::default()
            },
        );

        assert!(core.cover_store.item_cover(&book("Dune")).is_some());
        assert!(core.cover_store.item_cover(&book("Emma")).is_none());
        assert!(
            core.cover_store.item_cover(&video).is_some(),
            "another source's covers stay"
        );
    }

    #[tokio::test(flavor = "multi_thread")]
    async fn the_timer_relists_only_while_a_v6_client_is_connected() {
        let m = Arc::new(MockProviders::default());
        listing(&m, &["/b/1.mp3"]);
        let core = core_with(m.clone(), "timer");
        core.metadata_cache
            .media()
            .paths(m.as_ref(), MediaSource::Audiobooks)
            .unwrap();
        let listed = || {
            m.recorded()
                .iter()
                .filter(|c| c.starts_with("media_paths"))
                .count()
        };
        let task = tokio::spawn(run_every(
            core.clone(),
            Arc::new(Notify::new()),
            SHORT,
            SHORT,
        ));

        tokio::time::sleep(SHORT * 5).await;
        assert_eq!(listed(), 1, "nobody connected: nothing re-listed");

        let (tx, mut rx) = tokio::sync::mpsc::unbounded_channel::<String>();
        core.v6_broadcaster.register(1, tx);
        listing(&m, &["/b/1.mp3", "/b/2.mp3"]);
        tokio::time::sleep(SHORT * 10).await;
        task.abort();

        assert!(listed() > 1);
        assert_eq!(media_events(&mut rx).len(), 1);
    }
}
