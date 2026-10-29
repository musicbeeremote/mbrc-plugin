//! MusicBee's non-music library categories: audiobooks, videos and the inbox (#145).
//!
//! Served live, the way podcasts are. Each category's file list and tags are
//! held in memory only: nothing goes into redb, and `library_changes` stays
//! music only. MusicBee does not say which category a changed file is in, and
//! announces some changes not at all (a video dragged onto Videos sends
//! nothing), so a category is re-listed after a notification for a path
//! outside the music library, when a client asks for the sources, and on a
//! slow timer while a V6 client is connected. See `server::media` for the task
//! that drives it.

use std::collections::{BTreeSet, HashMap};
use std::sync::{Arc, Mutex, MutexGuard};
use std::time::{Duration, Instant};

use tokio::sync::Notify;

use crate::ffi::types::LibrarySource;
use crate::metadata_cache::CachedTags;
use crate::protocol::messages::TrackTags;
use crate::providers::Providers;

/// The shortest time between two rounds of re-listing.
pub const RELIST_EVERY: Duration = Duration::from_secs(30);

/// Files whose tags one host call reads, so a large category is many short
/// calls rather than one long one.
const TAG_BATCH: usize = 500;

/// A non-music library category, as the V6 `media_source` enum names it.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, PartialOrd, Ord)]
pub enum MediaSource {
    Audiobooks,
    Videos,
    Inbox,
}

impl MediaSource {
    pub const ALL: [Self; 3] = [Self::Audiobooks, Self::Videos, Self::Inbox];

    pub fn as_str(self) -> &'static str {
        match self {
            Self::Audiobooks => "audiobooks",
            Self::Videos => "videos",
            Self::Inbox => "inbox",
        }
    }

    pub fn parse(name: &str) -> Option<Self> {
        Self::ALL.into_iter().find(|s| s.as_str() == name)
    }

    fn category(self) -> LibrarySource {
        match self {
            Self::Audiobooks => LibrarySource::Audiobooks,
            Self::Videos => LibrarySource::Videos,
            Self::Inbox => LibrarySource::Inbox,
        }
    }
}

/// One category as last listed, with its tags once something needed them.
#[derive(Default)]
struct Listing {
    paths: Arc<Vec<String>>,
    tags: Option<Arc<MediaTags>>,
}

/// A category's tags in listing order: as the host read them, which the track
/// object is built from, and typed, which filters and sorts read.
pub struct MediaTags {
    pub raw: Vec<TrackTags>,
    pub typed: Vec<CachedTags>,
}

impl MediaTags {
    /// Each file's typed and raw tags, side by side.
    pub fn files(&self) -> impl Iterator<Item = (&CachedTags, &TrackTags)> {
        self.typed.iter().zip(&self.raw)
    }
}

/// What the media task has been asked to look at.
#[derive(Debug, Default, PartialEq, Eq)]
pub struct Pending {
    /// Categories to list again, for a change no listing can place.
    pub relist: BTreeSet<MediaSource>,
    /// Categories known to have changed, announced without a re-list.
    pub changed: BTreeSet<MediaSource>,
}

/// The categories' listings, kept in memory between requests.
#[derive(Default)]
pub struct MediaLibrary {
    listings: Mutex<HashMap<MediaSource, Listing>>,
    last_relist: Mutex<Option<Instant>>,
    pending: Mutex<Pending>,
    /// Wakes the media task for a change waiting in [`Pending`].
    pub nudge: Notify,
}

impl MediaLibrary {
    /// A category's files, listed now if it has not been yet.
    ///
    /// # Errors
    /// The host could not list the category.
    pub fn paths(
        &self,
        p: &dyn Providers,
        source: MediaSource,
    ) -> Result<Arc<Vec<String>>, String> {
        if let Some(listing) = lock(&self.listings).get(&source) {
            return Ok(listing.paths.clone());
        }
        let paths = Arc::new(p.media_paths(source.category())?);
        let mut listings = lock(&self.listings);
        let listing = listings.entry(source).or_insert_with(|| Listing {
            paths: paths.clone(),
            tags: None,
        });
        Ok(listing.paths.clone())
    }

    /// Every file's tags in a category, in listing order, read once per listing.
    ///
    /// # Errors
    /// The host could not list the category or read its tags.
    pub fn tags(&self, p: &dyn Providers, source: MediaSource) -> Result<Arc<MediaTags>, String> {
        let paths = self.paths(p, source)?;
        if let Some(tags) = lock(&self.listings)
            .get(&source)
            .and_then(|l| l.tags.clone())
        {
            return Ok(tags);
        }
        let mut raw = Vec::with_capacity(paths.len());
        for batch in paths.chunks(TAG_BATCH) {
            raw.extend(p.tracks_detailed_for_paths(batch.to_vec())?);
        }
        let typed = raw.iter().map(CachedTags::from).collect();
        let tags = Arc::new(MediaTags { raw, typed });
        if let Some(listing) = lock(&self.listings).get_mut(&source)
            && Arc::ptr_eq(&listing.paths, &paths)
        {
            listing.tags = Some(tags.clone());
        }
        Ok(tags)
    }

    /// Lists `sources` again and names those whose files changed.
    ///
    /// A category no client has read yet is stored without counting as a
    /// change: nobody holds a copy to refresh. One that fails to list keeps
    /// its last listing.
    pub fn relist(
        &self,
        p: &dyn Providers,
        sources: impl IntoIterator<Item = MediaSource>,
    ) -> Vec<MediaSource> {
        *lock(&self.last_relist) = Some(Instant::now());
        let mut changed = Vec::new();
        for source in sources {
            let Ok(fresh) = p.media_paths(source.category()) else {
                continue;
            };
            let mut listings = lock(&self.listings);
            if listings.get(&source).is_some_and(|l| *l.paths == fresh) {
                continue;
            }
            let listing = Listing {
                paths: Arc::new(fresh),
                tags: None,
            };
            if listings.insert(source, listing).is_some() {
                changed.push(source);
            }
        }
        changed
    }

    /// Whether any category has been listed, which is whether any client
    /// holds media to refresh.
    pub fn holds_listings(&self) -> bool {
        !lock(&self.listings).is_empty()
    }

    /// How long until the next re-list is allowed; `None` when it is now.
    pub fn until_relist(&self) -> Option<Duration> {
        let last = (*lock(&self.last_relist))?;
        RELIST_EVERY
            .checked_sub(last.elapsed())
            .filter(|d| !d.is_zero())
    }

    /// Library notifications for files outside the music library.
    ///
    /// A file a listing already holds had its tags edited or went away, so
    /// that category changed and its tags are read again. Any other file
    /// may be a new one in any category, which only a re-list can place.
    pub fn files_changed(&self, paths: &[String]) {
        if paths.is_empty() {
            return;
        }
        let mut changed = BTreeSet::new();
        let mut unplaced = false;
        {
            let mut listings = lock(&self.listings);
            let owner: HashMap<&str, MediaSource> = listings
                .iter()
                .flat_map(|(source, l)| l.paths.iter().map(move |p| (p.as_str(), *source)))
                .collect();
            for path in paths {
                match owner.get(path.as_str()) {
                    Some(source) => {
                        changed.insert(*source);
                    }
                    None => unplaced = true,
                }
            }
            drop(owner);
            for source in &changed {
                if let Some(listing) = listings.get_mut(source) {
                    listing.tags = None;
                }
            }
        }
        let mut pending = lock(&self.pending);
        pending.changed.extend(changed);
        if unplaced {
            pending.relist.extend(MediaSource::ALL);
        }
        drop(pending);
        self.nudge.notify_one();
    }

    /// Asks for `sources` to be listed again.
    pub fn request_relist(&self, sources: &[MediaSource]) {
        lock(&self.pending).relist.extend(sources.iter().copied());
        self.nudge.notify_one();
    }

    /// Takes what the media task has been asked to look at.
    pub fn take_pending(&self) -> Pending {
        std::mem::take(&mut *lock(&self.pending))
    }

    /// Drops every listing, for a switch to another library.
    pub fn forget(&self) {
        lock(&self.listings).clear();
        *lock(&self.last_relist) = None;
    }
}

/// The cover store key of a category's album, named by its [`album_key`] and
/// apart from the music library's.
///
/// [`album_key`]: crate::metadata_cache::album_key
pub fn album_cover_key(source: MediaSource, album_key: &str) -> String {
    format!("{}{album_key}", album_cover_prefix(source))
}

pub fn album_cover_prefix(source: MediaSource) -> String {
    format!("media:{}:", source.as_str())
}

fn lock<T>(mutex: &Mutex<T>) -> MutexGuard<'_, T> {
    mutex.lock().unwrap_or_else(|e| e.into_inner())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::providers::MockProviders;

    fn library(listings: &[(MediaSource, &[&str])]) -> MockProviders {
        let m = MockProviders::default();
        set(&m, listings);
        m
    }

    fn set(m: &MockProviders, listings: &[(MediaSource, &[&str])]) {
        let mut paths = m.media_paths.lock().unwrap();
        paths.clear();
        for (source, files) in listings {
            paths.insert(
                source.category() as i32,
                files.iter().map(|f| (*f).to_string()).collect(),
            );
        }
    }

    #[test]
    fn sources_round_trip_their_wire_names() {
        for source in MediaSource::ALL {
            assert_eq!(MediaSource::parse(source.as_str()), Some(source));
        }
        assert_eq!(MediaSource::parse("music"), None);
    }

    #[test]
    fn a_category_is_listed_once_until_it_changes() {
        let m = library(&[(MediaSource::Audiobooks, &["/a/1.mp3"])]);
        let media = MediaLibrary::default();

        media.paths(&m, MediaSource::Audiobooks).unwrap();
        media.paths(&m, MediaSource::Audiobooks).unwrap();

        let listed = m
            .recorded()
            .iter()
            .filter(|c| c.starts_with("media_paths"))
            .count();
        assert_eq!(listed, 1);
    }

    #[test]
    fn a_relist_names_only_the_categories_a_client_holds_that_changed() {
        let m = library(&[(MediaSource::Audiobooks, &["/a/1.mp3"])]);
        let media = MediaLibrary::default();
        media.paths(&m, MediaSource::Audiobooks).unwrap();

        set(
            &m,
            &[
                (MediaSource::Audiobooks, &["/a/1.mp3", "/a/2.mp3"]),
                (MediaSource::Inbox, &["/in/new.mp3"]),
            ],
        );

        assert_eq!(
            media.relist(&m, MediaSource::ALL),
            vec![MediaSource::Audiobooks]
        );
        assert_eq!(
            media.relist(&m, MediaSource::ALL),
            Vec::<MediaSource>::new()
        );
    }

    #[test]
    fn a_relist_waits_out_the_budget() {
        let m = library(&[]);
        let media = MediaLibrary::default();
        assert_eq!(media.until_relist(), None);

        media.relist(&m, MediaSource::ALL);

        assert!(media.until_relist().is_some());
    }

    #[test]
    fn an_edit_to_a_listed_file_marks_its_category_and_rereads_its_tags() {
        let m = library(&[(MediaSource::Videos, &["/v/clip.mp4"])]);
        let media = MediaLibrary::default();
        media.tags(&m, MediaSource::Videos).unwrap();

        media.files_changed(&["/v/clip.mp4".to_string()]);
        media.tags(&m, MediaSource::Videos).unwrap();

        let pending = media.take_pending();
        assert!(pending.relist.is_empty());
        assert_eq!(
            pending.changed.into_iter().collect::<Vec<_>>(),
            vec![MediaSource::Videos]
        );
        let reads = m
            .recorded()
            .iter()
            .filter(|c| c.starts_with("tracks_detailed"))
            .count();
        assert_eq!(reads, 2);
    }

    #[test]
    fn a_file_no_listing_holds_asks_for_a_relist() {
        let media = MediaLibrary::default();

        media.files_changed(&["/somewhere/new.mp3".to_string()]);

        assert_eq!(
            media.take_pending().relist.into_iter().collect::<Vec<_>>(),
            MediaSource::ALL
        );
    }
}
