//! Artist pictures (#116): what the store holds, and the queue that fills it.
//!
//! An artist is keyed by its credited name ([`crate::credits::key`]), so a
//! track split into `A` and `B` (#139) has pictures for each and never one for
//! the joined string. A reply only reads [`ArtistImages::hash_for`], a map
//! lookup. A missing picture is asked for with [`ArtistImages::request`], which
//! queues it for the background fetcher ([`super::artist_fetch`]) and returns.
//!
//! Pictures are written as the album covers are, one file per content hash, so
//! `cover_get` and `GET /api/cover/{hash}` serve them with no path of their
//! own, and the cover build's orphan prune keeps every hash listed here.
//!
//! Nothing here calls MusicBee: asking it for a picture downloads one inline,
//! which is the cost this design exists to avoid.

use std::collections::{HashMap, HashSet, VecDeque};
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Condvar, Mutex, RwLock, Weak};
use std::time::{Duration, Instant};

use redb::{Durability, ReadableTable};

use super::store::{CoverStore, now_unix_secs, write_cover};
use crate::credits;
use crate::store::{ARTIST_IMAGE, ARTIST_LOOKUP, Db};

/// The box an artist picture is fitted within, aspect kept, never upscaled.
pub const ARTIST_IMAGE_SIZE: u32 = 500;

/// The disk budget when the settings name none, in MiB.
pub const DEFAULT_BUDGET_MB: u64 = 100;

const DAY_SECS: i64 = 24 * 60 * 60;

/// How long a `not_found` or `ambiguous` answer is believed, and the longest
/// a run of errors pushes the next try out.
const MISS_RETRY_SECS: i64 = 30 * DAY_SECS;

/// How many client-requested artists may wait at once.
///
/// A client asks for the artists on screen, so a deeper queue only means one
/// that scrolled through the whole library; the overflow is asked again when
/// it is back on screen.
const MAX_REQUESTED: usize = 1000;

/// What the fetcher concluded about one artist.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Status {
    Found,
    NotFound,
    /// More than one artist, or none exactly, matched the name.
    Ambiguous,
    /// A source failed; asked again after a backoff.
    Error,
}

impl Status {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Found => "found",
            Self::NotFound => "not_found",
            Self::Ambiguous => "ambiguous",
            Self::Error => "error",
        }
    }

    fn parse(s: &str) -> Option<Self> {
        [Self::Found, Self::NotFound, Self::Ambiguous, Self::Error]
            .into_iter()
            .find(|status| status.as_str() == s)
    }
}

/// One artist's row in `ARTIST_LOOKUP`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Lookup {
    /// The MusicBrainz artist id, empty until one was resolved.
    pub mbid: String,
    /// Which source the picture came from, empty without one.
    pub source: String,
    pub status: Status,
    /// Unix seconds before which the artist is not fetched again.
    pub retry_after: i64,
    /// Consecutive [`Status::Error`] results, which set the backoff.
    pub errors: u32,
}

/// Why an artist is wanted, which decides how soon it is fetched.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Priority {
    /// On the playing track: ahead of everything a client asked for.
    NowPlaying,
    /// A client asked, for the artists it is showing.
    Requested,
}

#[derive(Debug, Clone)]
struct Stored {
    hash: String,
    bytes: u64,
}

/// One artist waiting for the fetcher, under the key it was queued by.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Queued {
    pub key: String,
    pub name: String,
    pub priority: Priority,
}

#[derive(Default)]
struct Queue {
    now_playing: VecDeque<Queued>,
    requested: VecDeque<Queued>,
    /// Keys waiting in either deque.
    queued: HashSet<String>,
}

impl Queue {
    fn push(&mut self, item: Queued) -> bool {
        if self.queued.contains(&item.key) {
            if item.priority == Priority::NowPlaying {
                self.requested.retain(|q| q.key != item.key);
                if !self.now_playing.iter().any(|q| q.key == item.key) {
                    self.now_playing.push_back(item);
                }
            }
            return true;
        }
        let key = item.key.clone();
        match item.priority {
            Priority::NowPlaying => self.now_playing.push_back(item),
            Priority::Requested if self.requested.len() >= MAX_REQUESTED => return false,
            Priority::Requested => self.requested.push_back(item),
        }
        self.queued.insert(key);
        true
    }

    fn pop(&mut self) -> Option<Queued> {
        let item = self
            .now_playing
            .pop_front()
            .or_else(|| self.requested.pop_front())?;
        self.queued.remove(&item.key);
        Some(item)
    }

    fn clear(&mut self) {
        *self = Self::default();
    }
}

/// The fetcher's side of the store: the queue, the switch, and who may run.
///
/// Kept apart from [`ArtistImages`] so the worker can hold it without holding
/// the cover store, and with it `mbrc.redb`: a core being reloaded must be
/// able to open the database again the moment the old one is dropped. The
/// worker reaches the store only through [`FetchQueue::with_store`].
#[derive(Default)]
pub struct FetchQueue {
    enabled: AtomicBool,
    queue: Mutex<Queue>,
    wake: Condvar,
    /// Bumped per fetcher started and by [`FetchQueue::retire`], so a fetcher
    /// that is no longer current exits.
    generation: AtomicU64,
    /// Held while a fetcher uses the store; [`FetchQueue::retire`] waits on it.
    store_access: Mutex<()>,
}

impl FetchQueue {
    pub fn is_enabled(&self) -> bool {
        self.enabled.load(Ordering::Acquire)
    }

    fn set_enabled(&self, enabled: bool) {
        self.enabled.store(enabled, Ordering::Release);
        if !enabled {
            self.lock_queue().clear();
        }
    }

    /// Queues `item`; `false` when downloading is off or the requested queue
    /// is full.
    pub fn push(&self, item: Queued) -> bool {
        if !self.is_enabled() {
            return false;
        }
        let queued = self.lock_queue().push(item);
        if queued {
            self.wake.notify_one();
        }
        queued
    }

    /// The next artist to fetch, waiting up to `timeout` for one.
    pub fn next(&self, timeout: Duration) -> Option<Queued> {
        let mut queue = self.lock_queue();
        if let Some(item) = queue.pop() {
            return Some(item);
        }
        queue = self
            .wake
            .wait_timeout(queue, timeout)
            .unwrap_or_else(|e| e.into_inner())
            .0;
        queue.pop()
    }

    /// Waits `pause` without fetching, returning early only when `generation`
    /// is retired.
    pub fn pause(&self, generation: u64, pause: Duration) {
        let until = Instant::now() + pause;
        let mut queue = self.lock_queue();
        while self.is_current(generation) {
            let left = until.saturating_duration_since(Instant::now());
            if left.is_zero() {
                return;
            }
            queue = self
                .wake
                .wait_timeout(queue, left)
                .unwrap_or_else(|e| e.into_inner())
                .0;
        }
    }

    /// Claims the fetcher slot, returning the generation the caller runs as.
    pub fn begin_worker(&self) -> u64 {
        self.generation.fetch_add(1, Ordering::AcqRel) + 1
    }

    /// Whether a fetcher started as `generation` is still the current one.
    pub fn is_current(&self, generation: u64) -> bool {
        self.generation.load(Ordering::Acquire) == generation
    }

    /// Stops every fetcher, returning once none holds the store.
    ///
    /// A fetcher checks its generation under `store_access` before it touches
    /// the store, so after this returns none will again.
    pub fn retire(&self) {
        self.generation.fetch_add(1, Ordering::AcqRel);
        {
            let _queue = self.lock_queue();
            self.wake.notify_all();
        }
        drop(self.store_access.lock().unwrap_or_else(|e| e.into_inner()));
    }

    /// Runs `f` on the artist store if `generation` is current and the store
    /// is still alive; `None` tells the fetcher to exit.
    pub fn with_store<T>(
        &self,
        generation: u64,
        store: &Weak<CoverStore>,
        f: impl FnOnce(&ArtistImages) -> T,
    ) -> Option<T> {
        let _access = self.store_access.lock().unwrap_or_else(|e| e.into_inner());
        if !self.is_current(generation) {
            return None;
        }
        let store = store.upgrade()?;
        Some(f(store.artists()))
    }

    fn lock_queue(&self) -> std::sync::MutexGuard<'_, Queue> {
        self.queue.lock().unwrap_or_else(|e| e.into_inner())
    }
}

/// The artist picture store, and the queue its fetcher works through.
pub struct ArtistImages {
    covers_dir: PathBuf,
    db: Db,
    budget: AtomicU64,
    /// The stored pictures' bytes, kept beside the map so the budget check
    /// does not walk it.
    used: AtomicU64,
    images: RwLock<HashMap<String, Stored>>,
    lookups: RwLock<HashMap<String, Lookup>>,
    queue: Arc<FetchQueue>,
}

impl ArtistImages {
    /// Opens the store over `db`, keeping only pictures whose file still exists.
    pub fn new(db: Db, covers_dir: PathBuf) -> Self {
        let images: HashMap<String, Stored> = load_images(&db)
            .into_iter()
            .filter_map(|(key, hash)| {
                let bytes = std::fs::metadata(covers_dir.join(&hash)).ok()?.len();
                Some((key, Stored { hash, bytes }))
            })
            .collect();
        let used = images.values().map(|s| s.bytes).sum();
        let lookups = load_lookups(&db);
        Self {
            covers_dir,
            db,
            budget: AtomicU64::new(DEFAULT_BUDGET_MB * 1024 * 1024),
            used: AtomicU64::new(used),
            images: RwLock::new(images),
            lookups: RwLock::new(lookups),
            queue: Arc::new(FetchQueue::default()),
        }
    }

    /// The fetch queue, for the worker to hold apart from the store.
    pub fn queue(&self) -> Arc<FetchQueue> {
        self.queue.clone()
    }

    /// Whether the user has allowed pictures to be downloaded.
    pub fn is_enabled(&self) -> bool {
        self.queue.is_enabled()
    }

    /// Turns downloading on or off. Off drops what is waiting and stops a fetch
    /// in progress before its next request; stored pictures are still served.
    pub fn set_enabled(&self, enabled: bool) {
        self.queue.set_enabled(enabled);
    }

    /// Whether a requested picture can arrive: downloading is on and the disk
    /// budget has room.
    pub fn is_accepting(&self) -> bool {
        self.is_enabled() && !self.over_budget()
    }

    pub fn set_budget(&self, bytes: u64) {
        self.budget.store(bytes, Ordering::Release);
    }

    /// Whether the stored pictures have used up the disk budget.
    pub fn over_budget(&self) -> bool {
        self.used_bytes() >= self.budget.load(Ordering::Acquire)
    }

    pub fn used_bytes(&self) -> u64 {
        self.used.load(Ordering::Acquire)
    }

    /// The stored picture's content hash for `name`, if there is one.
    pub fn hash_for(&self, name: &str) -> Option<String> {
        self.read_images()
            .get(&credits::key(name))
            .map(|s| s.hash.clone())
    }

    /// Every content hash the store references, for the cover prune to keep.
    pub fn hashes(&self) -> HashSet<String> {
        self.read_images()
            .values()
            .map(|s| s.hash.clone())
            .collect()
    }

    #[cfg(test)]
    pub fn lookup(&self, name: &str) -> Option<Lookup> {
        self.lookups
            .read()
            .unwrap_or_else(|e| e.into_inner())
            .get(&credits::key(name))
            .cloned()
    }

    /// Queues `name` for the fetcher unless no picture can arrive (see
    /// [`Self::is_accepting`]), it already has one, or its last miss is not due
    /// again. Returns whether it is waiting now.
    pub fn request(&self, name: &str, priority: Priority) -> bool {
        if !self.is_accepting() || !self.needs_fetch(name, now_unix_secs()) {
            return false;
        }
        self.queue.push(Queued {
            key: credits::key(name),
            name: name.trim().to_owned(),
            priority,
        })
    }

    /// Whether `name` has no picture and is due to be looked for at `now`.
    pub fn needs_fetch(&self, name: &str, now: i64) -> bool {
        let key = credits::key(name);
        if key.is_empty() || self.read_images().contains_key(&key) {
            return false;
        }
        self.lookups
            .read()
            .unwrap_or_else(|e| e.into_inner())
            .get(&key)
            .is_none_or(|l| l.status == Status::Found || l.retry_after <= now)
    }

    /// The next queued name, without waiting.
    #[cfg(test)]
    pub fn next_queued(&self) -> Option<String> {
        self.queue.next(Duration::ZERO).map(|q| q.name)
    }

    /// Fits `raw` within [`ARTIST_IMAGE_SIZE`], stores it as `name`'s picture,
    /// and returns its content hash.
    ///
    /// # Errors
    /// The image does not decode, or its file cannot be written.
    pub fn store(
        &self,
        name: &str,
        raw: &[u8],
        mbid: &str,
        source: &str,
    ) -> Result<String, String> {
        let (hash, resized) = write_cover(&self.covers_dir, raw, ARTIST_IMAGE_SIZE)?;
        let key = credits::key(name);
        let lookup = Lookup {
            mbid: mbid.to_owned(),
            source: source.to_owned(),
            status: Status::Found,
            retry_after: 0,
            errors: 0,
        };
        self.db.write(Durability::Immediate, |txn| {
            txn.open_table(ARTIST_IMAGE)?
                .insert(key.as_str(), hash.as_str())?;
            insert_lookup(txn, &key, &lookup)
        });
        let bytes = resized.jpeg.len() as u64;
        let replaced = self.write_images().insert(
            key.clone(),
            Stored {
                hash: hash.clone(),
                bytes,
            },
        );
        self.used.fetch_add(bytes, Ordering::AcqRel);
        if let Some(old) = replaced {
            self.used.fetch_sub(old.bytes, Ordering::AcqRel);
        }
        self.write_lookups().insert(key, lookup);
        Ok(hash)
    }

    /// Records that no picture was found for `name` this time, and when to look
    /// again: 30 days for a miss, a doubling backoff from one day for an error.
    pub fn record_miss(&self, name: &str, status: Status, mbid: &str) {
        let key = credits::key(name);
        let now = now_unix_secs();
        let previous_errors = self
            .lookups
            .read()
            .unwrap_or_else(|e| e.into_inner())
            .get(&key)
            .map_or(0, |l| l.errors);
        let (retry_after, errors) = match status {
            Status::Error => {
                let errors = previous_errors.saturating_add(1);
                (now + error_backoff_secs(errors), errors)
            }
            _ => (now + MISS_RETRY_SECS, 0),
        };
        let lookup = Lookup {
            mbid: mbid.to_owned(),
            source: String::new(),
            status,
            retry_after,
            errors,
        };
        self.db.write(Durability::Immediate, |txn| {
            insert_lookup(txn, &key, &lookup)
        });
        self.write_lookups().insert(key, lookup);
    }

    fn read_images(&self) -> std::sync::RwLockReadGuard<'_, HashMap<String, Stored>> {
        self.images.read().unwrap_or_else(|e| e.into_inner())
    }
    fn write_images(&self) -> std::sync::RwLockWriteGuard<'_, HashMap<String, Stored>> {
        self.images.write().unwrap_or_else(|e| e.into_inner())
    }
    fn write_lookups(&self) -> std::sync::RwLockWriteGuard<'_, HashMap<String, Lookup>> {
        self.lookups.write().unwrap_or_else(|e| e.into_inner())
    }
}

/// How long the `errors`th error in a row holds off the next try.
fn error_backoff_secs(errors: u32) -> i64 {
    let doublings = errors.saturating_sub(1).min(5);
    (DAY_SECS << doublings).min(MISS_RETRY_SECS)
}

fn insert_lookup(txn: &redb::WriteTransaction, key: &str, l: &Lookup) -> Result<(), redb::Error> {
    txn.open_table(ARTIST_LOOKUP)?.insert(
        key,
        (
            l.mbid.as_str(),
            l.source.as_str(),
            l.status.as_str(),
            l.retry_after,
            l.errors,
        ),
    )?;
    Ok(())
}

fn load_images(db: &Db) -> HashMap<String, String> {
    db.read(|txn| {
        let table = match txn.open_table(ARTIST_IMAGE) {
            Ok(t) => t,
            Err(redb::TableError::TableDoesNotExist(_)) => return Ok(HashMap::new()),
            Err(e) => return Err(e.into()),
        };
        let mut map = HashMap::new();
        for entry in table.iter()? {
            let (k, v) = entry?;
            map.insert(k.value().to_owned(), v.value().to_owned());
        }
        Ok(map)
    })
    .unwrap_or_default()
}

fn load_lookups(db: &Db) -> HashMap<String, Lookup> {
    db.read(|txn| {
        let table = match txn.open_table(ARTIST_LOOKUP) {
            Ok(t) => t,
            Err(redb::TableError::TableDoesNotExist(_)) => return Ok(HashMap::new()),
            Err(e) => return Err(e.into()),
        };
        let mut map = HashMap::new();
        for entry in table.iter()? {
            let (k, v) = entry?;
            let (mbid, source, status, retry_after, errors) = v.value();
            let Some(status) = Status::parse(status) else {
                continue;
            };
            map.insert(
                k.value().to_owned(),
                Lookup {
                    mbid: mbid.to_owned(),
                    source: source.to_owned(),
                    status,
                    retry_after,
                    errors,
                },
            );
        }
        Ok(map)
    })
    .unwrap_or_default()
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::cover::test_jpeg_bytes;

    fn temp_store(name: &str) -> (ArtistImages, Db, PathBuf) {
        let (db, dir) = crate::cover::test_storage(&format!("artist-images-{name}"));
        let covers = dir.join("cache").join("covers");
        let store = ArtistImages::new(db.clone(), covers.clone());
        store.set_enabled(true);
        (store, db, covers)
    }

    #[test]
    fn a_stored_picture_is_found_by_any_spelling_and_survives_a_restart() {
        let (store, db, covers) = temp_store("restart");
        let hash = store
            .store("Gadiel", &test_jpeg_bytes(800, 600), "mbid-1", "wikidata")
            .unwrap();
        assert_eq!(store.hash_for(" gadiel "), Some(hash.clone()));
        assert!(covers.join(&hash).exists());

        let reopened = ArtistImages::new(db, covers);
        assert_eq!(reopened.hash_for("GADIEL"), Some(hash));
        assert_eq!(reopened.lookup("Gadiel").unwrap().mbid, "mbid-1");
    }

    #[test]
    fn a_picture_is_fitted_within_500_pixels_keeping_its_aspect() {
        let (store, _db, covers) = temp_store("size");
        let hash = store
            .store("A", &test_jpeg_bytes(1000, 2000), "", "wikidata")
            .unwrap();
        let img = image::load_from_memory(&std::fs::read(covers.join(hash)).unwrap()).unwrap();
        assert_eq!((img.width(), img.height()), (250, 500));
    }

    #[test]
    fn a_picture_whose_file_is_gone_is_not_served_and_is_fetched_again() {
        let (store, db, covers) = temp_store("gone");
        let hash = store
            .store("A", &test_jpeg_bytes(64, 64), "", "wikidata")
            .unwrap();
        std::fs::remove_file(covers.join(hash)).unwrap();
        let reopened = ArtistImages::new(db, covers);
        assert_eq!(reopened.hash_for("A"), None);
        assert!(reopened.needs_fetch("A", now_unix_secs()));
    }

    #[test]
    fn a_miss_waits_30_days_and_an_error_backs_off_from_one_day() {
        let (store, _db, _) = temp_store("retry");
        let now = now_unix_secs();
        store.record_miss("Unknown", Status::NotFound, "");
        store.record_miss("Twins", Status::Ambiguous, "");
        assert!(!store.needs_fetch("Unknown", now + 29 * DAY_SECS));
        assert!(store.needs_fetch("Unknown", now + 31 * DAY_SECS));
        assert!(store.needs_fetch("Twins", now + 31 * DAY_SECS));

        store.record_miss("Flaky", Status::Error, "");
        assert!(store.needs_fetch("Flaky", now + DAY_SECS + 5));
        store.record_miss("Flaky", Status::Error, "");
        assert!(!store.needs_fetch("Flaky", now + DAY_SECS + 5));
        assert!(store.needs_fetch("Flaky", now + 2 * DAY_SECS + 5));
        assert_eq!(store.lookup("Flaky").unwrap().errors, 2);
        assert_eq!(error_backoff_secs(30), MISS_RETRY_SECS);
    }

    #[test]
    fn nothing_is_queued_while_downloading_is_off() {
        let (store, _db, _) = temp_store("off");
        store.set_enabled(false);
        assert!(!store.request("A", Priority::Requested));
        assert_eq!(store.next_queued(), None);
    }

    #[test]
    fn the_playing_artists_jump_the_queue_and_duplicates_are_dropped() {
        let (store, _db, _) = temp_store("queue");
        assert!(store.request("A", Priority::Requested));
        assert!(store.request("B", Priority::Requested));
        assert!(store.request("a", Priority::Requested));
        assert!(store.request("b", Priority::NowPlaying));
        assert!(store.request("C", Priority::NowPlaying));
        let order: Vec<String> = std::iter::from_fn(|| store.next_queued()).collect();
        assert_eq!(order, ["b", "C", "A"]);
    }

    #[test]
    fn an_artist_with_a_picture_or_a_recent_miss_is_not_queued() {
        let (store, _db, _) = temp_store("skip");
        store
            .store("A", &test_jpeg_bytes(64, 64), "", "wikidata")
            .unwrap();
        store.record_miss("B", Status::NotFound, "");
        assert!(!store.request("A", Priority::NowPlaying));
        assert!(!store.request("B", Priority::NowPlaying));
        assert!(!store.request("  ", Priority::NowPlaying));
    }

    #[test]
    fn a_full_budget_stops_queueing_and_says_so() {
        let (store, db, covers) = temp_store("budget");
        store.set_budget(1);
        assert!(store.is_accepting());
        let hash = store
            .store("A", &test_jpeg_bytes(64, 64), "", "wikidata")
            .unwrap();
        store
            .store("a", &test_jpeg_bytes(64, 64), "", "wikidata")
            .unwrap();
        let bytes = std::fs::metadata(covers.join(hash)).unwrap().len();
        assert_eq!(store.used_bytes(), bytes, "a replaced picture counts once");
        assert!(!store.is_accepting());
        assert!(!store.request("B", Priority::NowPlaying));
        assert_eq!(ArtistImages::new(db, covers).used_bytes(), bytes);
    }

    #[test]
    fn a_newer_worker_retires_the_older_one() {
        let (store, _db, _) = temp_store("worker");
        let queue = store.queue();
        let first = queue.begin_worker();
        let second = queue.begin_worker();
        assert!(!queue.is_current(first));
        assert!(queue.is_current(second));
        queue.retire();
        assert!(!queue.is_current(second));
    }

    #[test]
    fn a_retired_or_orphaned_worker_never_reaches_the_store() {
        let store = Arc::new(crate::cover::test_cover_store("artist-images-weak"));
        let weak = Arc::downgrade(&store);
        let queue = store.artists().queue();
        let generation = queue.begin_worker();
        assert_eq!(queue.with_store(generation, &weak, |_| 1), Some(1));
        queue.retire();
        assert_eq!(queue.with_store(generation, &weak, |_| 1), None);

        let generation = queue.begin_worker();
        drop(store);
        assert_eq!(queue.with_store(generation, &weak, |_| 1), None);
    }

    #[test]
    fn a_pause_ends_early_only_when_the_worker_is_retired() {
        let (store, _db, _) = temp_store("pause");
        let queue = store.queue();
        let generation = queue.begin_worker();
        assert!(store.request("A", Priority::Requested));
        let started = Instant::now();
        queue.pause(generation, Duration::from_millis(50));
        assert!(
            started.elapsed() >= Duration::from_millis(50),
            "a push does not end it"
        );

        let retirer = {
            let queue = queue.clone();
            std::thread::spawn(move || {
                std::thread::sleep(Duration::from_millis(20));
                queue.retire();
            })
        };
        let started = Instant::now();
        queue.pause(generation, Duration::from_secs(30));
        assert!(started.elapsed() < Duration::from_secs(5));
        retirer.join().unwrap();
    }
}
