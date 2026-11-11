//! Artist pictures (#116): what the store holds, and the queue that fills it.
//!
//! An artist is keyed by its lead artist ([`crate::credits::lead_artist`]), so
//! a track split into `A` and `B` (#139) has pictures for each and a name nobody
//! split ("A feat. B") shares A's. A reply only reads
//! [`ArtistImages::hash_for`], a map lookup. A missing picture is asked for with [`ArtistImages::request`], which
//! queues it for the background fetcher ([`super::artist_fetch`]) and returns.
//!
//! Pictures are written as the album covers are, one file per content hash, so
//! `cover_get` and `GET /api/cover/{hash}` serve them with no path of their
//! own, and the cover build's orphan prune keeps every hash listed here.
//!
//! MusicBee is asked only for its thumbnail, and only on the fetcher's thread:
//! about a second and 70 KB when it has to download one. Its full picture call
//! downloads a backdrop of up to 2 MB and is never used.

use std::collections::{HashMap, HashSet, VecDeque};
use std::path::{Path, PathBuf};
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

/// How long MusicBee's "no thumbnail" is believed before it is asked again.
///
/// Asking costs nothing once MusicBee has answered, but a client re-asks for
/// every artist it shows, and each answer is a call into MusicBee.
const MUSICBEE_RECHECK: Duration = Duration::from_secs(60 * 60);

/// How many client-requested artists may wait at once.
///
/// A client asks for the artists on screen, so a deeper queue only means one
/// that scrolled through the whole library; the overflow is asked again when
/// it is back on screen.
const MAX_REQUESTED: usize = 1000;

/// How many spellings of one artist a queued entry announces its picture under.
///
/// A client is told by the name it asked with, and a page may show several that
/// share a lead; one past this learns of the picture at its next request.
const MAX_SPELLINGS: usize = 16;

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

#[derive(Debug, Clone)]
struct Stored {
    hash: String,
    bytes: u64,
}

/// One artist waiting for the fetcher, under the key it was queued by.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Queued {
    pub key: String,
    /// Every spelling asked for, the first one as it was first queued.
    pub names: Vec<String>,
}

impl Queued {
    fn merge(&mut self, names: Vec<String>) {
        for name in names {
            if self.names.len() < MAX_SPELLINGS && !self.names.contains(&name) {
                self.names.push(name);
            }
        }
    }
}

#[derive(Default)]
struct Queue {
    waiting: VecDeque<Queued>,
    /// Keys in `waiting`.
    queued: HashSet<String>,
    /// Artists set aside until the web answers again; apart from `queued`, so a
    /// new request still gets its MusicBee turn.
    parked: Vec<Queued>,
}

impl Queue {
    fn push(&mut self, item: Queued) -> bool {
        if self.queued.contains(&item.key) {
            if let Some(waiting) = self.waiting.iter_mut().find(|q| q.key == item.key) {
                waiting.merge(item.names);
            }
            return true;
        }
        if self.waiting.len() >= MAX_REQUESTED {
            return false;
        }
        self.queued.insert(item.key.clone());
        self.waiting.push_back(item);
        true
    }

    fn pop(&mut self) -> Option<Queued> {
        let item = self.waiting.pop_front()?;
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
    retire_hook: Mutex<Option<RetireHook>>,
}

type RetireHook = Box<dyn FnOnce() + Send>;

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
    ///
    /// `generation` is checked under the queue lock [`Self::retire`] notifies
    /// under, so a retire cannot land unseen between a check and the wait.
    pub fn next(&self, generation: u64, timeout: Duration) -> Option<Queued> {
        let mut queue = self.lock_queue();
        if !self.is_current(generation) {
            return None;
        }
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

    /// Sets `item` aside until [`Self::unpark`], while the web is not answering;
    /// dropped when downloading is off.
    pub fn park(&self, item: Queued) {
        if !self.is_enabled() {
            return;
        }
        let mut queue = self.lock_queue();
        match queue.parked.iter_mut().find(|q| q.key == item.key) {
            Some(parked) => parked.merge(item.names),
            None => queue.parked.push(item),
        }
    }

    /// Queues again everything [`Self::park`] set aside.
    pub fn unpark(&self) {
        let parked = std::mem::take(&mut self.lock_queue().parked);
        for item in parked {
            self.push(item);
        }
    }

    /// Runs `hook` when this fetcher is retired, to stop what it is blocked in.
    pub fn on_retire(&self, hook: Box<dyn FnOnce() + Send>) {
        *self.lock_retire_hook() = Some(hook);
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
        let hook = self.lock_retire_hook().take();
        if let Some(hook) = hook {
            hook();
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

    /// How many artists are waiting for the fetcher.
    pub(crate) fn waiting(&self) -> usize {
        let queue = self.lock_queue();
        queue.waiting.len() + queue.parked.len()
    }

    fn lock_retire_hook(&self) -> std::sync::MutexGuard<'_, Option<RetireHook>> {
        self.retire_hook.lock().unwrap_or_else(|e| e.into_inner())
    }

    fn lock_queue(&self) -> std::sync::MutexGuard<'_, Queue> {
        self.queue.lock().unwrap_or_else(|e| e.into_inner())
    }
}

/// Which sources the fetcher may use, as the settings panel switches them.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Sources {
    /// MusicBee's own thumbnail.
    pub musicbee: bool,
    /// The backdrops MusicBee cached on disk.
    pub backdrops: bool,
    /// MusicBrainz, Wikidata and Commons.
    pub web: bool,
}

impl Default for Sources {
    fn default() -> Self {
        Self {
            musicbee: true,
            backdrops: false,
            web: true,
        }
    }
}

impl Sources {
    /// Whether either of MusicBee's sources is on.
    pub fn local(self) -> bool {
        self.musicbee || self.backdrops
    }

    /// Whether any source is on.
    pub fn any(self) -> bool {
        self.local() || self.web
    }
}

/// What the artist picture store holds and is waiting on, for the settings panel.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub(crate) struct Summary {
    pub stored: usize,
    pub used_bytes: u64,
    pub budget_bytes: u64,
    pub not_found: usize,
    pub ambiguous: usize,
    pub errors: usize,
    pub waiting: usize,
}

/// The artist picture store, and the queue its fetcher works through.
pub struct ArtistImages {
    covers_dir: PathBuf,
    db: Db,
    budget: AtomicU64,
    sources: RwLock<Sources>,
    /// The stored pictures' bytes, kept beside the map so the budget check
    /// does not walk it.
    used: AtomicU64,
    images: RwLock<HashMap<String, Stored>>,
    lookups: RwLock<HashMap<String, Lookup>>,
    /// When MusicBee last had no thumbnail or backdrop, by key: asked again only
    /// after [`MUSICBEE_RECHECK`], since a client re-asks for what it shows.
    musicbee_none: Mutex<HashMap<String, Instant>>,
    /// Held from writing a picture's file until its hash is listed, and by a
    /// cover prune, which would otherwise delete the file as an orphan.
    files: Mutex<()>,
    queue: Arc<FetchQueue>,
}

impl ArtistImages {
    /// Opens the store over `db`, keeping only pictures whose file still exists.
    pub fn new(db: Db, covers_dir: PathBuf) -> Self {
        let rows = load_images(&db);
        let sizes = file_sizes(&covers_dir, &rows.values().map(String::as_str).collect());
        let mut counted = HashSet::new();
        let images: HashMap<String, Stored> = rows
            .into_iter()
            .filter_map(|(key, hash)| {
                let size = *sizes.get(&hash)?;
                let bytes = if counted.insert(hash.clone()) {
                    size
                } else {
                    0
                };
                Some((key, Stored { hash, bytes }))
            })
            .collect();
        let used = images.values().map(|s| s.bytes).sum();
        let lookups = load_lookups(&db);
        Self {
            covers_dir,
            db,
            budget: AtomicU64::new(DEFAULT_BUDGET_MB * 1024 * 1024),
            sources: RwLock::new(Sources::default()),
            used: AtomicU64::new(used),
            images: RwLock::new(images),
            lookups: RwLock::new(lookups),
            musicbee_none: Mutex::new(HashMap::new()),
            files: Mutex::new(()),
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

    /// Whether a requested picture can arrive: downloading is on, a source is
    /// switched on, and the disk budget has room.
    pub fn is_accepting(&self) -> bool {
        self.is_enabled() && self.sources().any() && !self.over_budget()
    }

    pub fn set_budget(&self, bytes: u64) {
        self.budget.store(bytes, Ordering::Release);
    }

    pub fn sources(&self) -> Sources {
        *self.sources.read().unwrap_or_else(|e| e.into_inner())
    }

    /// Switches sources for the lookups that follow; a lookup in progress ends
    /// as it began.
    ///
    /// A MusicBee source switched on forgets when MusicBee last had nothing, since
    /// that answer came without it.
    pub fn set_sources(&self, sources: Sources) {
        let before = std::mem::replace(
            &mut *self.sources.write().unwrap_or_else(|e| e.into_inner()),
            sources,
        );
        if (sources.musicbee && !before.musicbee) || (sources.backdrops && !before.backdrops) {
            self.lock_musicbee_none().clear();
        }
    }

    /// Whether the stored pictures have used up the disk budget.
    pub fn over_budget(&self) -> bool {
        self.used_bytes() >= self.budget.load(Ordering::Acquire)
    }

    pub fn used_bytes(&self) -> u64 {
        self.used.load(Ordering::Acquire)
    }

    pub(crate) fn summary(&self) -> Summary {
        let mut summary = Summary {
            stored: self.read_images().len(),
            used_bytes: self.used_bytes(),
            budget_bytes: self.budget.load(Ordering::Acquire),
            waiting: self.queue.waiting(),
            ..Summary::default()
        };
        for lookup in self
            .lookups
            .read()
            .unwrap_or_else(|e| e.into_inner())
            .values()
        {
            match lookup.status {
                Status::NotFound => summary.not_found += 1,
                Status::Ambiguous => summary.ambiguous += 1,
                Status::Error => summary.errors += 1,
                Status::Found => {}
            }
        }
        summary
    }

    /// The stored picture's content hash for `name`, if there is one.
    pub fn hash_for(&self, name: &str) -> Option<String> {
        self.read_images()
            .get(&picture_key(name))
            .map(|s| s.hash.clone())
    }

    /// Holds off storing a picture while a cover prune decides what to delete.
    pub(super) fn hold_files(&self) -> std::sync::MutexGuard<'_, ()> {
        self.files.lock().unwrap_or_else(|e| e.into_inner())
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
            .get(&picture_key(name))
            .cloned()
    }

    /// Queues `name` for the fetcher unless no picture can arrive (see
    /// [`Self::is_accepting`]), it has one, or no source is due (see
    /// [`Self::is_due`]). Returns whether it is waiting now.
    pub fn request(&self, name: &str) -> bool {
        if !self.is_accepting() || !self.is_due(name, now_unix_secs()) {
            return false;
        }
        self.queue.push(Queued {
            key: picture_key(name),
            names: vec![name.trim().to_owned()],
        })
    }

    /// Whether `name` names an artist with no stored picture.
    pub fn lacks_picture(&self, name: &str) -> bool {
        let key = picture_key(name);
        !key.is_empty() && !self.read_images().contains_key(&key)
    }

    /// Whether `name` has no picture and a source is due to be asked: MusicBee
    /// an hour after it last had none, the web once a miss expires.
    pub fn is_due(&self, name: &str, now: i64) -> bool {
        self.lacks_picture(name) && (self.musicbee_due(name) || self.web_due(name, now))
    }

    /// Whether a MusicBee source is on and has not lately had nothing for `name`.
    pub fn musicbee_due(&self, name: &str) -> bool {
        self.sources().local()
            && self
                .lock_musicbee_none()
                .get(&picture_key(name))
                .is_none_or(|at| at.elapsed() >= MUSICBEE_RECHECK)
    }

    /// Records that MusicBee has no thumbnail or backdrop of `name`.
    pub fn musicbee_has_none(&self, name: &str) {
        self.lock_musicbee_none()
            .insert(picture_key(name), Instant::now());
    }

    /// Whether `name` has no picture and its web lookup is due at `now`.
    pub fn web_due(&self, name: &str, now: i64) -> bool {
        let key = picture_key(name);
        self.sources().web
            && !key.is_empty()
            && !self.read_images().contains_key(&key)
            && self
                .lookups
                .read()
                .unwrap_or_else(|e| e.into_inner())
                .get(&key)
                .is_none_or(|l| l.status == Status::Found || l.retry_after <= now)
    }

    /// The next queued name, without waiting.
    #[cfg(test)]
    pub fn next_queued(&self) -> Option<String> {
        let item = self.queue.lock_queue().pop()?;
        item.names.into_iter().next()
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
        let _files = self.hold_files();
        let (hash, resized) = write_cover(&self.covers_dir, raw, ARTIST_IMAGE_SIZE)?;
        let key = picture_key(name);
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
        let shared = self
            .read_images()
            .iter()
            .any(|(k, s)| *k != key && s.hash == hash);
        let bytes = if shared { 0 } else { resized.jpeg.len() as u64 };
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
        let key = picture_key(name);
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

    /// Forgets every picture and lookup, returning the hashes the pictures had;
    /// `None`, with nothing forgotten, when `mbrc.redb` could not be written.
    ///
    /// The caller holds [`Self::hold_files`] and deletes the files no album
    /// cover shares, so a fetch landing meanwhile cannot lose its file.
    pub(super) fn forget_all(&self) -> Option<HashSet<String>> {
        let written = self.db.try_write(Durability::Immediate, |txn| {
            txn.delete_table(ARTIST_IMAGE)?;
            txn.delete_table(ARTIST_LOOKUP)?;
            Ok(())
        });
        if !written {
            return None;
        }
        let hashes = std::mem::take(&mut *self.write_images())
            .into_values()
            .map(|stored| stored.hash)
            .collect();
        self.write_lookups().clear();
        self.used.store(0, Ordering::Release);
        self.lock_musicbee_none().clear();
        self.queue.lock_queue().clear();
        Some(hashes)
    }

    fn lock_musicbee_none(&self) -> std::sync::MutexGuard<'_, HashMap<String, Instant>> {
        self.musicbee_none.lock().unwrap_or_else(|e| e.into_inner())
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

/// The key an artist's picture is kept under: its lead artist's, so a name
/// nobody split ("A feat. B") is served A's picture and looked up as A.
fn picture_key(name: &str) -> String {
    credits::key(credits::lead_artist(name))
}

/// The size of each `wanted` file under `dir`, from one directory read: a store
/// of thousands of pictures opens during plugin startup, where a stat per
/// picture would show.
fn file_sizes(dir: &Path, wanted: &HashSet<&str>) -> HashMap<String, u64> {
    let Some(entries) = (!wanted.is_empty())
        .then(|| std::fs::read_dir(dir).ok())
        .flatten()
    else {
        return HashMap::new();
    };
    entries
        .flatten()
        .filter_map(|entry| {
            let name = entry.file_name().into_string().ok()?;
            let size = entry.metadata().ok()?.len();
            wanted.contains(name.as_str()).then_some((name, size))
        })
        .collect()
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

    fn temp_store(name: &str) -> (crate::test_dir::TestDir, ArtistImages, Db, PathBuf) {
        let (dir, db) = crate::cover::test_storage(&format!("artist-images-{name}"));
        let covers = dir.join("cache").join("covers");
        let store = ArtistImages::new(db.clone(), covers.clone());
        store.set_enabled(true);
        (dir, store, db, covers)
    }

    #[test]
    fn a_stored_picture_is_found_by_any_spelling_and_survives_a_restart() {
        let (_dir, store, db, covers) = temp_store("restart");
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
        let (_dir, store, _db, covers) = temp_store("size");
        let hash = store
            .store("A", &test_jpeg_bytes(1000, 2000), "", "wikidata")
            .unwrap();
        let img = image::load_from_memory(&std::fs::read(covers.join(hash)).unwrap()).unwrap();
        assert_eq!((img.width(), img.height()), (250, 500));
    }

    #[test]
    fn a_picture_whose_file_is_gone_is_not_served_and_is_fetched_again() {
        let (_dir, store, db, covers) = temp_store("gone");
        let hash = store
            .store("A", &test_jpeg_bytes(64, 64), "", "wikidata")
            .unwrap();
        std::fs::remove_file(covers.join(hash)).unwrap();
        let reopened = ArtistImages::new(db, covers);
        assert_eq!(reopened.hash_for("A"), None);
        assert!(reopened.web_due("A", now_unix_secs()));
    }

    #[test]
    fn with_every_source_switched_off_nothing_is_queued() {
        let (_dir, store, _db, _) = temp_store("sources-off");
        store.set_sources(Sources {
            musicbee: false,
            backdrops: false,
            web: false,
        });
        assert!(!store.request("Gadiel"));
        store.set_sources(Sources {
            backdrops: true,
            ..Sources::default()
        });
        assert!(store.request("Gadiel"));
    }

    #[test]
    fn switching_a_musicbee_source_on_asks_musicbee_again() {
        let (_dir, store, _db, _) = temp_store("source-on");
        store.set_sources(Sources {
            musicbee: false,
            ..Sources::default()
        });
        store.musicbee_has_none("Gadiel");
        assert!(!store.musicbee_due("Gadiel"));

        store.set_sources(Sources::default());
        assert!(store.musicbee_due("Gadiel"));
    }

    #[test]
    fn a_miss_waits_30_days_and_an_error_backs_off_from_one_day() {
        let (_dir, store, _db, _) = temp_store("retry");
        let now = now_unix_secs();
        store.record_miss("Unknown", Status::NotFound, "");
        store.record_miss("Twins", Status::Ambiguous, "");
        assert!(!store.web_due("Unknown", now + 29 * DAY_SECS));
        assert!(store.web_due("Unknown", now + 31 * DAY_SECS));
        assert!(store.web_due("Twins", now + 31 * DAY_SECS));

        store.record_miss("Flaky", Status::Error, "");
        assert!(store.web_due("Flaky", now + DAY_SECS + 5));
        store.record_miss("Flaky", Status::Error, "");
        assert!(!store.web_due("Flaky", now + DAY_SECS + 5));
        assert!(store.web_due("Flaky", now + 2 * DAY_SECS + 5));
        assert_eq!(store.lookup("Flaky").unwrap().errors, 2);
        assert_eq!(error_backoff_secs(30), MISS_RETRY_SECS);
    }

    #[test]
    fn nothing_is_queued_while_downloading_is_off() {
        let (_dir, store, _db, _) = temp_store("off");
        store.set_enabled(false);
        assert!(!store.request("A"));
        assert_eq!(store.next_queued(), None);
    }

    #[test]
    fn artists_are_fetched_in_the_order_asked_and_duplicates_are_dropped() {
        let (_dir, store, _db, _) = temp_store("queue");
        assert!(store.request("A"));
        assert!(store.request("B"));
        assert!(store.request("a"));
        assert!(store.request("C"));
        let order: Vec<String> = std::iter::from_fn(|| store.next_queued()).collect();
        assert_eq!(order, ["A", "B", "C"]);
    }

    #[test]
    fn a_queued_artist_keeps_every_spelling_it_was_asked_for_under() {
        let (_dir, store, _db, _) = temp_store("spellings");
        let queue = store.queue();
        store.request("Tico");
        store.request("Tico feat. Gadiel");
        store.request("Tico");
        store.request("Tico ft. Yandel");
        let item = queue.next(queue.begin_worker(), Duration::ZERO).unwrap();
        assert_eq!(item.names, ["Tico", "Tico feat. Gadiel", "Tico ft. Yandel"]);

        for i in 0..2 * MAX_SPELLINGS {
            store.request(&format!("Gadiel feat. {i}"));
        }
        let item = queue.next(queue.begin_worker(), Duration::ZERO).unwrap();
        assert_eq!(item.names.len(), MAX_SPELLINGS);
    }

    #[test]
    fn a_parked_artist_waits_counted_until_it_is_let_back() {
        let (_dir, store, _db, _) = temp_store("parked");
        let queue = store.queue();
        let generation = queue.begin_worker();
        store.request("A");
        let item = queue.next(generation, Duration::ZERO).unwrap();

        queue.park(item);
        assert_eq!(store.summary().waiting, 1);
        assert!(queue.next(generation, Duration::ZERO).is_none());

        queue.unpark();
        let item = queue.next(generation, Duration::ZERO).unwrap();

        store.set_enabled(false);
        queue.park(item);
        assert_eq!(store.summary().waiting, 0, "off drops it");
    }

    #[test]
    fn a_picture_is_not_stored_while_a_prune_holds_the_files() {
        let (_dir, store, _db, covers) = temp_store("prune-hold");
        let held = store.hold_files();
        std::thread::scope(|scope| {
            let storing =
                scope.spawn(|| store.store("A", &test_jpeg_bytes(64, 64), "", "wikidata"));
            std::thread::sleep(Duration::from_millis(50));
            assert!(!covers.exists(), "no file yet for the prune to take");
            drop(held);
            let hash = storing.join().unwrap().unwrap();
            assert!(covers.join(hash).exists());
        });
    }

    #[test]
    fn an_artist_with_a_picture_is_not_queued_but_one_that_missed_is() {
        let (_dir, store, _db, _) = temp_store("skip");
        store
            .store("A", &test_jpeg_bytes(64, 64), "", "wikidata")
            .unwrap();
        store.record_miss("B", Status::NotFound, "");
        assert!(!store.request("A"));
        assert!(!store.request("  "));
        assert!(store.request("B"), "MusicBee may have one since");
        assert!(!store.web_due("B", now_unix_secs()), "the web waits");
    }

    #[test]
    fn the_summary_counts_pictures_misses_and_what_waits() {
        let (_dir, store, _db, _) = temp_store("summary");
        store.set_budget(1 << 20);
        store
            .store("A", &test_jpeg_bytes(64, 64), "", "wikidata")
            .unwrap();
        store.record_miss("B", Status::NotFound, "");
        store.record_miss("C", Status::Ambiguous, "");
        store.record_miss("D", Status::Error, "");
        store.request("E");

        let summary = store.summary();
        assert_eq!(
            summary,
            Summary {
                stored: 1,
                used_bytes: store.used_bytes(),
                budget_bytes: 1 << 20,
                not_found: 1,
                ambiguous: 1,
                errors: 1,
                waiting: 1,
            }
        );
    }

    #[test]
    fn a_featured_name_is_served_and_counted_as_its_lead_artist() {
        let (_dir, store, _db, _) = temp_store("lead");
        let hash = store
            .store("Adelitas Way", &test_jpeg_bytes(64, 64), "", "musicbee")
            .unwrap();
        store.set_budget(1);

        let joined = "adelitas way FEAT. Tyler Connolly";
        assert_eq!(
            store.hash_for(joined),
            Some(hash),
            "even with the budget full"
        );
        assert!(!store.request(joined));
        assert_eq!(store.summary().stored, 1);
    }

    #[test]
    fn two_artists_with_the_same_picture_count_its_file_once() {
        let (_dir, store, db, covers) = temp_store("same-file");
        store
            .store("Adam Skorupa", &test_jpeg_bytes(64, 64), "", "musicbee")
            .unwrap();
        let used = store.used_bytes();
        store
            .store(
                "Adam Skorupa & Krzysztof Wierzynkiewicz",
                &test_jpeg_bytes(64, 64),
                "",
                "musicbee",
            )
            .unwrap();

        assert_eq!(store.used_bytes(), used);
        assert_eq!(ArtistImages::new(db, covers).used_bytes(), used);
    }

    #[test]
    fn musicbee_is_asked_again_only_after_an_hour_once_it_said_none() {
        let (_dir, store, _db, _) = temp_store("musicbee-none");
        store.record_miss("Jormundgard", Status::NotFound, "");
        assert!(store.request("Jormundgard"));
        let _ = store.next_queued();

        store.musicbee_has_none("Jormundgard");
        assert!(!store.request("Jormundgard"), "nothing is due");
        assert!(store.request("Tico"), "the web is still due for Tico");
    }

    #[test]
    fn a_full_budget_stops_queueing_and_says_so() {
        let (_dir, store, db, covers) = temp_store("budget");
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
        assert!(!store.request("B"));
        assert_eq!(ArtistImages::new(db, covers).used_bytes(), bytes);
    }

    #[test]
    fn a_newer_worker_retires_the_older_one() {
        let (_dir, store, _db, _) = temp_store("worker");
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
        let (_dir, store) = crate::cover::test_cover_store("artist-images-weak").into_parts();
        let store = Arc::new(store);
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
    fn a_retired_worker_never_starts_waiting_for_work() {
        let (_dir, store, _db, _) = temp_store("retired-wait");
        let queue = store.queue();
        let generation = queue.begin_worker();
        queue.retire();
        let started = Instant::now();
        assert!(queue.next(generation, Duration::from_secs(30)).is_none());
        assert!(started.elapsed() < Duration::from_secs(5));
    }

    #[test]
    fn retiring_runs_the_hook_that_stops_a_blocked_fetch_once() {
        let (_dir, store, _db, _) = temp_store("retire-hook");
        let queue = store.queue();
        let runs = Arc::new(AtomicU64::new(0));
        let counted = runs.clone();
        queue.on_retire(Box::new(move || {
            counted.fetch_add(1, Ordering::AcqRel);
        }));
        queue.retire();
        queue.retire();
        assert_eq!(runs.load(Ordering::Acquire), 1);
    }
}
