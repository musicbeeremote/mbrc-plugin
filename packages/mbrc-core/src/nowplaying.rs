//! In-memory now-playing + player-state cache.
//!
//! The core HOLDS the now-playing state instead of pulling it over FFI on every
//! client request. Reads (init, `nowplaying*` queries, `playerstatus`) serve
//! from here; the FFI is used only to keep the mirror fresh:
//! - **event-driven** writers refresh a slice when MusicBee fires the matching
//!   notification (track change, play-state/volume/mute, artwork/lyrics ready);
//! - the **poll** (`server::monitor`) is the sole writer for shuffle/repeat/
//!   scrobble, which fire no MusicBee event.
//!
//! Position is deliberately NOT cached: it advances continuously and stays
//! computed/polled.
//!
//! Why this matters: a warm FFI round-trip is ~microseconds, but the first
//! `NowPlaying_GetDownloadedLyrics` for a track blocks ~2.7s inside MusicBee.
//! Seeding once on a background task (and refreshing on track change) pays that
//! cost off the request path, so `init` never blocks on it.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, RwLock};

use crate::protocol::messages::{
    Cover, LastfmStatus, Lyrics, PlayerState, TrackDetails, TrackInfo,
};
use crate::providers::Providers;

/// The cached snapshot. Every field mirrors what the shipped plugin would return
/// for the current track / player. Position is intentionally absent.
#[derive(Default, Clone)]
pub struct NowPlaying {
    pub track_info: TrackInfo,
    pub track_details: TrackDetails,
    pub rating: String,
    pub lfm: LastfmStatus,
    pub cover: Cover,
    pub lyrics: Lyrics,
    pub player: PlayerState,
}

/// The cache: the provider boundary (for refreshes + lazy seed), the guarded
/// snapshot, and a "seeded" flag so the first read fills a cold cache.
pub struct NowPlayingCache {
    providers: Arc<dyn Providers>,
    state: RwLock<NowPlaying>,
    seeded: AtomicBool,
}

impl NowPlayingCache {
    pub fn new(providers: Arc<dyn Providers>) -> Self {
        Self {
            providers,
            state: RwLock::new(NowPlaying::default()),
            seeded: AtomicBool::new(false),
        }
    }

    // ── Writers (FFI -> cache) ───────────────────────────────────────
    // Each fetches OUTSIDE the lock, then takes the write lock briefly. On a
    // provider error the old value is kept (a transient FFI failure never blanks
    // the cache). The 2.7s downloaded-lyrics fetch happens here, off the read
    // path.

    /// Full refresh: every cached field. Startup seed and a belt-and-braces
    /// resync. Marks the cache seeded.
    pub fn refresh_all(&self) {
        let p = &*self.providers;
        let track_info = p.track_info().ok();
        let track_details = p.track_details().ok();
        let rating = p.rating().ok();
        let lfm = p.lfm_rating().ok();
        let cover = p.cover().ok();
        let lyrics = p.lyrics().ok();
        let player = p.player_state().ok();
        {
            let mut s = self.write();
            set(&mut s.track_info, track_info);
            set(&mut s.track_details, track_details);
            set(&mut s.rating, rating);
            set(&mut s.lfm, lfm);
            set(&mut s.cover, cover);
            set(&mut s.lyrics, lyrics);
            set(&mut s.player, player);
        }
        self.seeded.store(true, Ordering::Release);
    }

    /// Track-scoped refresh (MusicBee `TrackChanged`): everything that changes
    /// with the track. Cover/lyrics may still be empty here if MusicBee hasn't
    /// finished fetching them - the `*Ready` notifications refresh them later.
    pub fn refresh_track_bundle(&self) {
        let p = &*self.providers;
        let track_info = p.track_info().ok();
        let track_details = p.track_details().ok();
        let rating = p.rating().ok();
        let lfm = p.lfm_rating().ok();
        let cover = p.cover().ok();
        let lyrics = p.lyrics().ok();
        let mut s = self.write();
        set(&mut s.track_info, track_info);
        set(&mut s.track_details, track_details);
        set(&mut s.rating, rating);
        set(&mut s.lfm, lfm);
        set(&mut s.cover, cover);
        set(&mut s.lyrics, lyrics);
    }

    /// Refresh the cover slice (MusicBee `NowPlayingArtworkReady`).
    pub fn refresh_cover(&self) {
        let cover = self.providers.cover().ok();
        set(&mut self.write().cover, cover);
    }

    /// Refresh the lyrics slice (MusicBee `NowPlayingLyricsReady`).
    pub fn refresh_lyrics(&self) {
        let lyrics = self.providers.lyrics().ok();
        set(&mut self.write().lyrics, lyrics);
    }

    /// Refresh the whole player slice (play-state/volume/mute notifications and
    /// the shuffle/repeat/scrobble poll). `player_state()` returns every player
    /// field in one FFI call, so one refresh serves all of them.
    pub fn refresh_player(&self) {
        let player = self.providers.player_state().ok();
        set(&mut self.write().player, player);
    }

    /// Store an already-fetched player state (the monitor poll already queried
    /// it for change-detection; avoid a second FFI call).
    pub fn set_player(&self, player: PlayerState) {
        self.write().player = player;
    }

    // ── Readers (cache, with cold-cache fill) ────────────────────────

    /// A cloned snapshot of the whole cache (seeding if cold).
    pub fn snapshot(&self) -> NowPlaying {
        self.ensure_seeded();
        self.read().clone()
    }

    pub fn track_info(&self) -> TrackInfo {
        self.ensure_seeded();
        self.read().track_info.clone()
    }
    pub fn track_details(&self) -> TrackDetails {
        self.ensure_seeded();
        self.read().track_details.clone()
    }
    pub fn rating(&self) -> String {
        self.ensure_seeded();
        self.read().rating.clone()
    }
    pub fn lfm(&self) -> LastfmStatus {
        self.ensure_seeded();
        self.read().lfm
    }
    pub fn cover(&self) -> Cover {
        self.ensure_seeded();
        self.read().cover.clone()
    }
    pub fn lyrics(&self) -> Lyrics {
        self.ensure_seeded();
        self.read().lyrics.clone()
    }
    pub fn player(&self) -> PlayerState {
        self.ensure_seeded();
        self.read().player.clone()
    }

    /// Fill a cold cache on first read. Idempotent; a benign race just refreshes
    /// twice once.
    fn ensure_seeded(&self) {
        if !self.seeded.load(Ordering::Acquire) {
            self.refresh_all();
        }
    }

    fn read(&self) -> std::sync::RwLockReadGuard<'_, NowPlaying> {
        self.state.read().unwrap_or_else(|e| e.into_inner())
    }
    fn write(&self) -> std::sync::RwLockWriteGuard<'_, NowPlaying> {
        self.state.write().unwrap_or_else(|e| e.into_inner())
    }
}

/// Overwrite `slot` only when the refresh produced a value; a provider error
/// (`None`) leaves the previous value in place.
fn set<T>(slot: &mut T, value: Option<T>) {
    if let Some(v) = value {
        *slot = v;
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::{PlayState, ShuffleMode};
    use crate::providers::MockProviders;

    fn cache_with(m: MockProviders) -> NowPlayingCache {
        NowPlayingCache::new(Arc::new(m))
    }

    #[test]
    fn cold_read_seeds_from_providers() {
        let m = MockProviders {
            rating: "4".into(),
            player_state: PlayerState {
                volume: 42,
                ..Default::default()
            },
            ..Default::default()
        };
        let cache = cache_with(m);
        // First read fills the cache from the provider.
        assert_eq!(cache.rating(), "4");
        assert_eq!(cache.player().volume, 42);
    }

    #[test]
    fn refresh_player_updates_only_player_slice() {
        let m = MockProviders {
            rating: "3".into(),
            player_state: PlayerState {
                volume: 10,
                shuffle: ShuffleMode::Off,
                ..Default::default()
            },
            ..Default::default()
        };
        let cache = cache_with(m);
        cache.refresh_all();
        assert_eq!(cache.player().volume, 10);
        assert_eq!(cache.rating(), "3");

        // A new player state comes in via a notification-style refresh.
        cache.set_player(PlayerState {
            volume: 90,
            play_state: PlayState::Playing,
            ..Default::default()
        });
        assert_eq!(cache.player().volume, 90);
        assert_eq!(cache.player().play_state, PlayState::Playing);
        // The track-scoped fields are untouched.
        assert_eq!(cache.rating(), "3");
    }

    #[test]
    fn provider_error_keeps_previous_value() {
        // Seed a good value, then make the provider fail and refresh: the cached
        // value must survive.
        let cache = cache_with(MockProviders {
            rating: "5".into(),
            ..Default::default()
        });
        cache.refresh_all();
        assert_eq!(cache.rating(), "5");

        // set(None) is what a failed refresh does; assert it is a no-op.
        let mut slot = String::from("5");
        set(&mut slot, None::<String>);
        assert_eq!(slot, "5");
    }
}
