//! The provider boundary: the trait every command handler depends on, so
//! handlers can be unit-tested with zero C ABI, zero MusicBee, zero sockets.
//!
//! Implementations:
//! - [`FfiProviders`] wraps [`SafeCallbacks`] and talks to the real C# shim.
//! - [`NullProviders`] is a benign no-op placeholder (returns defaults), usable
//!   wherever a real provider isn't needed (e.g. the handshake integration test).
//! - `MockProviders` (test-only) returns configurable canned data + records calls.
//!
//! Slices 2/3 grow this trait one domain at a time.

use crate::ffi::callbacks::SafeCallbacks;
use crate::ffi::dtos::{
    AlbumCoverParams, BatchMetadataParams, BrowseParams, IndexParams, MoveParams,
    NowPlayingQueueParams, PaginationParams, PathParams, PathsParams, QueryParams, SetBoolParams,
    SetIntParams, SetLfmRatingParams, SetRepeatParams, StringValueParams, SyncDeltaParams,
    TagChangeParams,
};
use crate::ffi::types::{CommandType, QueryType};
use crate::protocol::messages::{
    AlbumCover, AlbumCoverItem, AlbumData, AlbumIdentifier, ArtistData, Cover, GenreData,
    LastfmStatus, Lyrics, NowPlayingListTrack, OutputDevices, Page, PlaybackPositionResponse,
    PlayerState, Playlist, QueueType, RadioStation, RepeatMode, SyncDelta, Track, TrackDetails,
    TrackInfo, TrackMetadata,
};

/// The MusicBee data/command surface, as the core sees it. Handlers take
/// `&dyn Providers` (or `&impl Providers`) and never touch the raw FFI.
pub trait Providers: Send + Sync {
    // Player transport.
    fn play(&self) -> Result<(), String>;
    fn pause(&self) -> Result<(), String>;
    fn play_pause(&self) -> Result<(), String>;
    fn stop(&self) -> Result<(), String>;
    fn next(&self) -> Result<(), String>;
    fn previous(&self) -> Result<(), String>;
    fn set_volume(&self, volume: i32) -> Result<(), String>;
    fn set_position(&self, position_ms: i32) -> Result<(), String>;

    // Player state + modes.
    fn player_state(&self) -> Result<PlayerState, String>;
    fn set_mute(&self, value: bool) -> Result<(), String>;
    fn set_shuffle(&self, value: bool) -> Result<(), String>;
    /// Enable/disable AutoDJ. Used by the shuffle command's AutoDJ cycle
    /// (off -> shuffle -> autodj -> off), which all V4 clients negotiate.
    fn set_auto_dj(&self, value: bool) -> Result<(), String>;
    fn set_repeat(&self, mode: RepeatMode) -> Result<(), String>;
    fn set_scrobble(&self, value: bool) -> Result<(), String>;

    // Output devices.
    fn output_devices(&self) -> Result<OutputDevices, String>;
    fn switch_output(&self, device: &str) -> Result<(), String>;

    // Now playing - track.
    fn playback_position(&self) -> Result<PlaybackPositionResponse, String>;
    fn track_info(&self) -> Result<TrackInfo, String>;
    fn track_details(&self) -> Result<TrackDetails, String>;
    fn cover(&self) -> Result<Cover, String>;
    fn lyrics(&self) -> Result<Lyrics, String>;
    fn rating(&self) -> Result<String, String>;
    fn set_rating(&self, value: &str) -> Result<(), String>;
    fn lfm_rating(&self) -> Result<LastfmStatus, String>;
    fn set_lfm_rating(&self, status: LastfmStatus) -> Result<(), String>;
    fn set_tag(&self, tag: &str, value: &str) -> Result<(), String>;

    // Now playing - list. `now_playing_list` is the sequential page (Android);
    // `now_playing_list_ordered` anchors at the current track and carries
    // album/album_artist (iOS). The handler picks by platform.
    fn now_playing_list(
        &self,
        offset: i32,
        limit: i32,
    ) -> Result<Page<NowPlayingListTrack>, String>;
    fn now_playing_list_ordered(
        &self,
        offset: i32,
        limit: i32,
    ) -> Result<Page<NowPlayingListTrack>, String>;
    fn play_list_item(&self, index: i32) -> Result<(), String>;
    fn remove_list_item(&self, index: i32) -> Result<(), String>;
    fn move_list_item(&self, from: i32, to: i32) -> Result<(), String>;
    fn search_list(&self, query: &str) -> Result<(), String>;
    fn queue(&self, queue_type: QueueType, files: Vec<String>, play: &str) -> Result<(), String>;

    // Library - flat browse (paginated).
    fn browse_genres(&self, offset: i32, limit: i32) -> Result<Page<GenreData>, String>;
    fn browse_artists(
        &self,
        offset: i32,
        limit: i32,
        album_artists: bool,
    ) -> Result<Page<ArtistData>, String>;
    fn browse_albums(&self, offset: i32, limit: i32) -> Result<Page<AlbumData>, String>;
    fn browse_tracks(&self, offset: i32, limit: i32) -> Result<Page<Track>, String>;

    // Library - hierarchical navigation (iOS; non-paginated).
    fn genre_artists(&self, genre: &str) -> Result<Vec<ArtistData>, String>;
    fn artist_albums(&self, artist: &str) -> Result<Vec<AlbumData>, String>;
    fn album_tracks(&self, album: &str) -> Result<Vec<Track>, String>;

    // Library - covers, radio, play-all.
    fn album_cover(&self, artist: &str, album: &str, hash: &str) -> Result<AlbumCover, String>;
    fn album_cover_page(&self, offset: i32, limit: i32) -> Result<Page<AlbumCoverItem>, String>;
    fn cover_cache_status(&self) -> Result<bool, String>;

    // Cover-cache leaf providers (single-pass host queries feeding the core's
    // `CoverStore`; the core owns resize/hash/cache). `album_identifiers` is ONE
    // library scan folded into per-album identities (the host used to make 2-3
    // passes); `artwork_raw` returns a track's raw MusicBee artwork as base64;
    // `batch_metadata` resolves paths to `{artist, album}` for the cover grid.
    fn album_identifiers(&self) -> Result<Vec<AlbumIdentifier>, String>;
    fn artwork_raw(&self, path: &str) -> Result<String, String>;
    fn batch_metadata(&self, paths: Vec<String>) -> Result<Vec<TrackMetadata>, String>;

    // Library cache (MBRCIP-0001). `track_paths` returns every track path in
    // browse order (the ordinal index, source of truth). `tracks_for_paths`
    // batch-reads the 7 browse tags for just a page's paths (bounded to O(page)).
    // `sync_delta` lists library changes since a watermark for the Scanner.
    fn track_paths(&self) -> Result<Vec<String>, String>;
    fn tracks_for_paths(&self, paths: Vec<String>) -> Result<Vec<Track>, String>;
    fn sync_delta(&self, updated_since: i64) -> Result<SyncDelta, String>;

    fn radio_stations(&self, offset: i32, limit: i32) -> Result<Page<RadioStation>, String>;
    fn play_all(&self, shuffle: bool) -> Result<(), String>;

    // Playlists.
    fn playlists(&self, offset: i32, limit: i32) -> Result<Page<Playlist>, String>;
    fn play_playlist(&self, url: &str) -> Result<(), String>;

    // System.
    fn plugin_version(&self) -> Result<String, String>;
    /// Set MusicBee's status-bar background-task message (host-only UI). Used to
    /// surface cover-cache build progress. Fire-and-forget.
    fn set_background_task_message(&self, message: &str) -> Result<(), String>;

    /// Push a core -> host UI event (fire-and-forget) so an open panel can
    /// refresh. Defaults to a no-op; only the FFI provider forwards it to the
    /// host's `on_event` callback. See [`HostEventType`](crate::ffi::types::HostEventType).
    fn emit_event(&self, _event_type: crate::ffi::types::HostEventType, _payload: &[u8]) {}
}

/// Production implementation: every method routes through the frozen C ABI.
pub struct FfiProviders {
    callbacks: SafeCallbacks,
}

impl FfiProviders {
    pub fn new(callbacks: SafeCallbacks) -> Self {
        Self { callbacks }
    }
}

impl Providers for FfiProviders {
    fn play(&self) -> Result<(), String> {
        self.callbacks.execute_command(CommandType::Play, &())
    }
    fn pause(&self) -> Result<(), String> {
        self.callbacks.execute_command(CommandType::Pause, &())
    }
    fn play_pause(&self) -> Result<(), String> {
        self.callbacks.execute_command(CommandType::PlayPause, &())
    }
    fn stop(&self) -> Result<(), String> {
        self.callbacks.execute_command(CommandType::Stop, &())
    }
    fn next(&self) -> Result<(), String> {
        self.callbacks.execute_command(CommandType::Next, &())
    }
    fn previous(&self) -> Result<(), String> {
        self.callbacks.execute_command(CommandType::Previous, &())
    }
    fn set_volume(&self, volume: i32) -> Result<(), String> {
        self.callbacks
            .execute_command(CommandType::SetVolume, &SetIntParams { value: volume })
    }
    fn set_position(&self, position_ms: i32) -> Result<(), String> {
        self.callbacks.execute_command(
            CommandType::SetPosition,
            &SetIntParams { value: position_ms },
        )
    }

    fn player_state(&self) -> Result<PlayerState, String> {
        self.callbacks.query_no_params(QueryType::PlayerState)
    }
    fn set_mute(&self, value: bool) -> Result<(), String> {
        self.callbacks
            .execute_command(CommandType::SetMute, &SetBoolParams { value })
    }
    fn set_shuffle(&self, value: bool) -> Result<(), String> {
        self.callbacks
            .execute_command(CommandType::SetShuffle, &SetBoolParams { value })
    }
    fn set_auto_dj(&self, value: bool) -> Result<(), String> {
        self.callbacks
            .execute_command(CommandType::SetAutoDj, &SetBoolParams { value })
    }
    fn set_repeat(&self, mode: RepeatMode) -> Result<(), String> {
        self.callbacks
            .execute_command(CommandType::SetRepeat, &SetRepeatParams { mode })
    }
    fn set_scrobble(&self, value: bool) -> Result<(), String> {
        self.callbacks
            .execute_command(CommandType::SetScrobble, &SetBoolParams { value })
    }

    fn output_devices(&self) -> Result<OutputDevices, String> {
        self.callbacks.query_no_params(QueryType::OutputDevices)
    }
    fn switch_output(&self, device: &str) -> Result<(), String> {
        self.callbacks.execute_command(
            CommandType::OutputSwitch,
            &StringValueParams {
                value: device.to_string(),
            },
        )
    }

    fn playback_position(&self) -> Result<PlaybackPositionResponse, String> {
        self.callbacks.query_no_params(QueryType::PlaybackPosition)
    }
    fn track_info(&self) -> Result<TrackInfo, String> {
        self.callbacks.query_no_params(QueryType::TrackInfo)
    }
    fn track_details(&self) -> Result<TrackDetails, String> {
        self.callbacks.query_no_params(QueryType::NowPlayingDetails)
    }
    fn cover(&self) -> Result<Cover, String> {
        self.callbacks.query_no_params(QueryType::CoverData)
    }
    fn lyrics(&self) -> Result<Lyrics, String> {
        self.callbacks.query_no_params(QueryType::Lyrics)
    }
    fn rating(&self) -> Result<String, String> {
        self.callbacks.query_no_params(QueryType::NowPlayingRating)
    }
    fn set_rating(&self, value: &str) -> Result<(), String> {
        self.callbacks.execute_command(
            CommandType::SetRating,
            &StringValueParams {
                value: value.to_string(),
            },
        )
    }
    fn lfm_rating(&self) -> Result<LastfmStatus, String> {
        self.callbacks
            .query_no_params(QueryType::NowPlayingLfmRating)
    }
    fn set_lfm_rating(&self, status: LastfmStatus) -> Result<(), String> {
        self.callbacks
            .execute_command(CommandType::SetLfmRating, &SetLfmRatingParams { status })
    }
    fn set_tag(&self, tag: &str, value: &str) -> Result<(), String> {
        self.callbacks.execute_command(
            CommandType::NowPlayingTagChange,
            &TagChangeParams {
                tag: tag.to_string(),
                value: value.to_string(),
            },
        )
    }

    fn now_playing_list(
        &self,
        offset: i32,
        limit: i32,
    ) -> Result<Page<NowPlayingListTrack>, String> {
        self.callbacks.query(
            QueryType::NowPlayingList,
            &PaginationParams { offset, limit },
        )
    }
    fn now_playing_list_ordered(
        &self,
        offset: i32,
        limit: i32,
    ) -> Result<Page<NowPlayingListTrack>, String> {
        self.callbacks.query(
            QueryType::NowPlayingListOrdered,
            &PaginationParams { offset, limit },
        )
    }
    fn play_list_item(&self, index: i32) -> Result<(), String> {
        self.callbacks
            .execute_command(CommandType::NowPlayingListPlay, &IndexParams { index })
    }
    fn remove_list_item(&self, index: i32) -> Result<(), String> {
        self.callbacks
            .execute_command(CommandType::NowPlayingListRemove, &IndexParams { index })
    }
    fn move_list_item(&self, from: i32, to: i32) -> Result<(), String> {
        self.callbacks
            .execute_command(CommandType::NowPlayingListMove, &MoveParams { from, to })
    }
    fn search_list(&self, query: &str) -> Result<(), String> {
        self.callbacks.execute_command(
            CommandType::NowPlayingListSearch,
            &StringValueParams {
                value: query.to_string(),
            },
        )
    }
    fn queue(&self, queue_type: QueueType, files: Vec<String>, play: &str) -> Result<(), String> {
        self.callbacks.execute_command(
            CommandType::NowPlayingQueue,
            &NowPlayingQueueParams {
                queue_type,
                files,
                play: play.to_string(),
            },
        )
    }

    fn browse_genres(&self, offset: i32, limit: i32) -> Result<Page<GenreData>, String> {
        self.callbacks.query(
            QueryType::LibraryBrowseGenres,
            &PaginationParams { offset, limit },
        )
    }
    fn browse_artists(
        &self,
        offset: i32,
        limit: i32,
        album_artists: bool,
    ) -> Result<Page<ArtistData>, String> {
        self.callbacks.query(
            QueryType::LibraryBrowseArtists,
            &BrowseParams {
                offset,
                limit,
                album_artists,
            },
        )
    }
    fn browse_albums(&self, offset: i32, limit: i32) -> Result<Page<AlbumData>, String> {
        self.callbacks.query(
            QueryType::LibraryBrowseAlbums,
            &PaginationParams { offset, limit },
        )
    }
    fn browse_tracks(&self, offset: i32, limit: i32) -> Result<Page<Track>, String> {
        self.callbacks.query(
            QueryType::LibraryBrowseTracks,
            &PaginationParams { offset, limit },
        )
    }
    fn genre_artists(&self, genre: &str) -> Result<Vec<ArtistData>, String> {
        self.callbacks.query(
            QueryType::LibraryGenreArtists,
            &QueryParams {
                query: genre.to_string(),
            },
        )
    }
    fn artist_albums(&self, artist: &str) -> Result<Vec<AlbumData>, String> {
        self.callbacks.query(
            QueryType::LibraryArtistAlbums,
            &QueryParams {
                query: artist.to_string(),
            },
        )
    }
    fn album_tracks(&self, album: &str) -> Result<Vec<Track>, String> {
        self.callbacks.query(
            QueryType::LibraryAlbumTracks,
            &QueryParams {
                query: album.to_string(),
            },
        )
    }
    fn album_cover(&self, artist: &str, album: &str, hash: &str) -> Result<AlbumCover, String> {
        self.callbacks.query(
            QueryType::AlbumCover,
            &AlbumCoverParams {
                artist: artist.to_string(),
                album: album.to_string(),
                client_hash: hash.to_string(),
            },
        )
    }
    fn album_cover_page(&self, offset: i32, limit: i32) -> Result<Page<AlbumCoverItem>, String> {
        self.callbacks.query(
            QueryType::AlbumCoverBatch,
            &PaginationParams { offset, limit },
        )
    }
    fn cover_cache_status(&self) -> Result<bool, String> {
        self.callbacks
            .query_no_params(QueryType::CoverCacheBuildStatus)
    }
    fn album_identifiers(&self) -> Result<Vec<AlbumIdentifier>, String> {
        self.callbacks.query_no_params(QueryType::AlbumIdentifiers)
    }
    fn artwork_raw(&self, path: &str) -> Result<String, String> {
        self.callbacks.query(
            QueryType::ArtworkRawForPath,
            &PathParams {
                path: path.to_string(),
            },
        )
    }
    fn batch_metadata(&self, paths: Vec<String>) -> Result<Vec<TrackMetadata>, String> {
        self.callbacks
            .query(QueryType::BatchMetadata, &BatchMetadataParams { paths })
    }
    fn track_paths(&self) -> Result<Vec<String>, String> {
        self.callbacks.query_no_params(QueryType::LibraryTrackPaths)
    }
    fn tracks_for_paths(&self, paths: Vec<String>) -> Result<Vec<Track>, String> {
        self.callbacks
            .query(QueryType::LibraryTracksForPaths, &PathsParams { paths })
    }
    fn sync_delta(&self, updated_since: i64) -> Result<SyncDelta, String> {
        self.callbacks.query(
            QueryType::LibrarySyncDelta,
            &SyncDeltaParams { updated_since },
        )
    }
    fn radio_stations(&self, offset: i32, limit: i32) -> Result<Page<RadioStation>, String> {
        self.callbacks.query(
            QueryType::RadioStations,
            &PaginationParams { offset, limit },
        )
    }
    fn play_all(&self, shuffle: bool) -> Result<(), String> {
        self.callbacks.execute_command(
            CommandType::LibraryPlayAll,
            &SetBoolParams { value: shuffle },
        )
    }
    fn playlists(&self, offset: i32, limit: i32) -> Result<Page<Playlist>, String> {
        self.callbacks
            .query(QueryType::PlaylistList, &PaginationParams { offset, limit })
    }
    fn play_playlist(&self, url: &str) -> Result<(), String> {
        self.callbacks.execute_command(
            CommandType::PlaylistPlay,
            &StringValueParams {
                value: url.to_string(),
            },
        )
    }
    fn plugin_version(&self) -> Result<String, String> {
        self.callbacks.query_no_params(QueryType::PluginVersion)
    }
    fn set_background_task_message(&self, message: &str) -> Result<(), String> {
        self.callbacks.execute_command(
            CommandType::SetBackgroundTaskMessage,
            &StringValueParams {
                value: message.to_string(),
            },
        )
    }
    fn emit_event(&self, event_type: crate::ffi::types::HostEventType, payload: &[u8]) {
        self.callbacks.emit_event(event_type, payload);
    }
}

/// A no-op provider: queries return defaults, commands succeed. A benign
/// placeholder for contexts that don't drive MusicBee (handshake-only tests,
/// future stand-ins). Grows with the trait so callers don't have to.
pub struct NullProviders;

impl Providers for NullProviders {
    fn play(&self) -> Result<(), String> {
        Ok(())
    }
    fn pause(&self) -> Result<(), String> {
        Ok(())
    }
    fn play_pause(&self) -> Result<(), String> {
        Ok(())
    }
    fn stop(&self) -> Result<(), String> {
        Ok(())
    }
    fn next(&self) -> Result<(), String> {
        Ok(())
    }
    fn previous(&self) -> Result<(), String> {
        Ok(())
    }
    fn set_volume(&self, _volume: i32) -> Result<(), String> {
        Ok(())
    }
    fn set_position(&self, _position_ms: i32) -> Result<(), String> {
        Ok(())
    }
    fn player_state(&self) -> Result<PlayerState, String> {
        Ok(PlayerState::default())
    }
    fn set_mute(&self, _value: bool) -> Result<(), String> {
        Ok(())
    }
    fn set_shuffle(&self, _value: bool) -> Result<(), String> {
        Ok(())
    }
    fn set_auto_dj(&self, _value: bool) -> Result<(), String> {
        Ok(())
    }
    fn set_repeat(&self, _mode: RepeatMode) -> Result<(), String> {
        Ok(())
    }
    fn set_scrobble(&self, _value: bool) -> Result<(), String> {
        Ok(())
    }
    fn output_devices(&self) -> Result<OutputDevices, String> {
        Ok(OutputDevices::default())
    }
    fn switch_output(&self, _device: &str) -> Result<(), String> {
        Ok(())
    }
    fn playback_position(&self) -> Result<PlaybackPositionResponse, String> {
        Ok(PlaybackPositionResponse::default())
    }
    fn track_info(&self) -> Result<TrackInfo, String> {
        Ok(TrackInfo::default())
    }
    fn track_details(&self) -> Result<TrackDetails, String> {
        Ok(TrackDetails::default())
    }
    fn cover(&self) -> Result<Cover, String> {
        Ok(Cover::default())
    }
    fn lyrics(&self) -> Result<Lyrics, String> {
        Ok(Lyrics::default())
    }
    fn rating(&self) -> Result<String, String> {
        Ok(String::new())
    }
    fn set_rating(&self, _value: &str) -> Result<(), String> {
        Ok(())
    }
    fn lfm_rating(&self) -> Result<LastfmStatus, String> {
        Ok(LastfmStatus::default())
    }
    fn set_lfm_rating(&self, _status: LastfmStatus) -> Result<(), String> {
        Ok(())
    }
    fn set_tag(&self, _tag: &str, _value: &str) -> Result<(), String> {
        Ok(())
    }
    fn now_playing_list(
        &self,
        _offset: i32,
        _limit: i32,
    ) -> Result<Page<NowPlayingListTrack>, String> {
        Ok(Page::default())
    }
    fn now_playing_list_ordered(
        &self,
        _offset: i32,
        _limit: i32,
    ) -> Result<Page<NowPlayingListTrack>, String> {
        Ok(Page::default())
    }
    fn play_list_item(&self, _index: i32) -> Result<(), String> {
        Ok(())
    }
    fn remove_list_item(&self, _index: i32) -> Result<(), String> {
        Ok(())
    }
    fn move_list_item(&self, _from: i32, _to: i32) -> Result<(), String> {
        Ok(())
    }
    fn search_list(&self, _query: &str) -> Result<(), String> {
        Ok(())
    }
    fn queue(
        &self,
        _queue_type: QueueType,
        _files: Vec<String>,
        _play: &str,
    ) -> Result<(), String> {
        Ok(())
    }
    fn browse_genres(&self, _offset: i32, _limit: i32) -> Result<Page<GenreData>, String> {
        Ok(Page::default())
    }
    fn browse_artists(
        &self,
        _offset: i32,
        _limit: i32,
        _album_artists: bool,
    ) -> Result<Page<ArtistData>, String> {
        Ok(Page::default())
    }
    fn browse_albums(&self, _offset: i32, _limit: i32) -> Result<Page<AlbumData>, String> {
        Ok(Page::default())
    }
    fn browse_tracks(&self, _offset: i32, _limit: i32) -> Result<Page<Track>, String> {
        Ok(Page::default())
    }
    fn genre_artists(&self, _genre: &str) -> Result<Vec<ArtistData>, String> {
        Ok(Vec::new())
    }
    fn artist_albums(&self, _artist: &str) -> Result<Vec<AlbumData>, String> {
        Ok(Vec::new())
    }
    fn album_tracks(&self, _album: &str) -> Result<Vec<Track>, String> {
        Ok(Vec::new())
    }
    fn album_cover(&self, _artist: &str, _album: &str, _hash: &str) -> Result<AlbumCover, String> {
        Ok(AlbumCover::default())
    }
    fn album_cover_page(&self, _offset: i32, _limit: i32) -> Result<Page<AlbumCoverItem>, String> {
        Ok(Page::default())
    }
    fn cover_cache_status(&self) -> Result<bool, String> {
        Ok(false)
    }
    fn album_identifiers(&self) -> Result<Vec<AlbumIdentifier>, String> {
        Ok(Vec::new())
    }
    fn artwork_raw(&self, _path: &str) -> Result<String, String> {
        Ok(String::new())
    }
    fn batch_metadata(&self, _paths: Vec<String>) -> Result<Vec<TrackMetadata>, String> {
        Ok(Vec::new())
    }
    fn track_paths(&self) -> Result<Vec<String>, String> {
        Ok(Vec::new())
    }
    fn tracks_for_paths(&self, _paths: Vec<String>) -> Result<Vec<Track>, String> {
        Ok(Vec::new())
    }
    fn sync_delta(&self, _updated_since: i64) -> Result<SyncDelta, String> {
        Ok(SyncDelta::default())
    }
    fn radio_stations(&self, _offset: i32, _limit: i32) -> Result<Page<RadioStation>, String> {
        Ok(Page::default())
    }
    fn play_all(&self, _shuffle: bool) -> Result<(), String> {
        Ok(())
    }
    fn playlists(&self, _offset: i32, _limit: i32) -> Result<Page<Playlist>, String> {
        Ok(Page::default())
    }
    fn play_playlist(&self, _url: &str) -> Result<(), String> {
        Ok(())
    }
    fn plugin_version(&self) -> Result<String, String> {
        Ok(String::new())
    }
    fn set_background_task_message(&self, _message: &str) -> Result<(), String> {
        Ok(())
    }
}

/// Test double: returns configurable canned data and records the calls made.
#[cfg(test)]
#[derive(Default)]
pub struct MockProviders {
    pub player_state: PlayerState,
    pub output_devices: OutputDevices,
    pub position: PlaybackPositionResponse,
    pub track_info: TrackInfo,
    pub track_details: TrackDetails,
    pub cover: Cover,
    pub lyrics: Lyrics,
    pub rating: String,
    pub lfm_rating: LastfmStatus,
    pub now_playing_list: Page<NowPlayingListTrack>,
    pub now_playing_list_ordered: Page<NowPlayingListTrack>,
    pub browse_genres: Page<GenreData>,
    pub browse_artists: Page<ArtistData>,
    pub browse_albums: Page<AlbumData>,
    pub browse_tracks: Page<Track>,
    pub genre_artists: Vec<ArtistData>,
    pub artist_albums: Vec<AlbumData>,
    pub album_tracks: Vec<Track>,
    pub album_cover: AlbumCover,
    pub album_cover_page: Page<AlbumCoverItem>,
    pub cover_cache_status: bool,
    pub album_identifiers: Vec<AlbumIdentifier>,
    pub artwork_raw: String,
    pub batch_metadata: Vec<TrackMetadata>,
    pub track_paths: Vec<String>,
    pub tracks_for_paths: Vec<Track>,
    pub sync_delta: SyncDelta,
    pub radio_stations: Page<RadioStation>,
    pub playlists: Page<Playlist>,
    pub plugin_version: String,
    pub calls: std::sync::Mutex<Vec<String>>,
}

#[cfg(test)]
impl MockProviders {
    fn record(&self, name: impl Into<String>) {
        self.calls.lock().unwrap().push(name.into());
    }
    pub fn recorded(&self) -> Vec<String> {
        self.calls.lock().unwrap().clone()
    }
}

#[cfg(test)]
impl Providers for MockProviders {
    fn play(&self) -> Result<(), String> {
        self.record("play");
        Ok(())
    }
    fn pause(&self) -> Result<(), String> {
        self.record("pause");
        Ok(())
    }
    fn play_pause(&self) -> Result<(), String> {
        self.record("play_pause");
        Ok(())
    }
    fn stop(&self) -> Result<(), String> {
        self.record("stop");
        Ok(())
    }
    fn next(&self) -> Result<(), String> {
        self.record("next");
        Ok(())
    }
    fn previous(&self) -> Result<(), String> {
        self.record("previous");
        Ok(())
    }
    fn set_volume(&self, volume: i32) -> Result<(), String> {
        self.record(format!("set_volume({volume})"));
        Ok(())
    }
    fn set_position(&self, position_ms: i32) -> Result<(), String> {
        self.record(format!("set_position({position_ms})"));
        Ok(())
    }
    fn player_state(&self) -> Result<PlayerState, String> {
        self.record("player_state");
        Ok(self.player_state.clone())
    }
    fn set_mute(&self, value: bool) -> Result<(), String> {
        self.record(format!("set_mute({value})"));
        Ok(())
    }
    fn set_shuffle(&self, value: bool) -> Result<(), String> {
        self.record(format!("set_shuffle({value})"));
        Ok(())
    }
    fn set_auto_dj(&self, value: bool) -> Result<(), String> {
        self.record(format!("set_auto_dj({value})"));
        Ok(())
    }
    fn set_repeat(&self, mode: RepeatMode) -> Result<(), String> {
        self.record(format!("set_repeat({mode:?})"));
        Ok(())
    }
    fn set_scrobble(&self, value: bool) -> Result<(), String> {
        self.record(format!("set_scrobble({value})"));
        Ok(())
    }
    fn output_devices(&self) -> Result<OutputDevices, String> {
        self.record("output_devices");
        Ok(self.output_devices.clone())
    }
    fn switch_output(&self, device: &str) -> Result<(), String> {
        self.record(format!("switch_output({device})"));
        Ok(())
    }
    fn playback_position(&self) -> Result<PlaybackPositionResponse, String> {
        self.record("playback_position");
        Ok(self.position.clone())
    }
    fn track_info(&self) -> Result<TrackInfo, String> {
        self.record("track_info");
        Ok(self.track_info.clone())
    }
    fn track_details(&self) -> Result<TrackDetails, String> {
        self.record("track_details");
        Ok(self.track_details.clone())
    }
    fn cover(&self) -> Result<Cover, String> {
        self.record("cover");
        Ok(self.cover.clone())
    }
    fn lyrics(&self) -> Result<Lyrics, String> {
        self.record("lyrics");
        Ok(self.lyrics.clone())
    }
    fn rating(&self) -> Result<String, String> {
        self.record("rating");
        Ok(self.rating.clone())
    }
    fn set_rating(&self, value: &str) -> Result<(), String> {
        self.record(format!("set_rating({value})"));
        Ok(())
    }
    fn lfm_rating(&self) -> Result<LastfmStatus, String> {
        self.record("lfm_rating");
        Ok(self.lfm_rating)
    }
    fn set_lfm_rating(&self, status: LastfmStatus) -> Result<(), String> {
        self.record(format!("set_lfm_rating({status:?})"));
        Ok(())
    }
    fn set_tag(&self, tag: &str, value: &str) -> Result<(), String> {
        self.record(format!("set_tag({tag},{value})"));
        Ok(())
    }
    fn now_playing_list(
        &self,
        _offset: i32,
        _limit: i32,
    ) -> Result<Page<NowPlayingListTrack>, String> {
        self.record("now_playing_list");
        Ok(self.now_playing_list.clone())
    }
    fn now_playing_list_ordered(
        &self,
        _offset: i32,
        _limit: i32,
    ) -> Result<Page<NowPlayingListTrack>, String> {
        self.record("now_playing_list_ordered");
        Ok(self.now_playing_list_ordered.clone())
    }
    fn play_list_item(&self, index: i32) -> Result<(), String> {
        self.record(format!("play_list_item({index})"));
        Ok(())
    }
    fn remove_list_item(&self, index: i32) -> Result<(), String> {
        self.record(format!("remove_list_item({index})"));
        Ok(())
    }
    fn move_list_item(&self, from: i32, to: i32) -> Result<(), String> {
        self.record(format!("move_list_item({from},{to})"));
        Ok(())
    }
    fn search_list(&self, query: &str) -> Result<(), String> {
        self.record(format!("search_list({query})"));
        Ok(())
    }
    fn queue(&self, queue_type: QueueType, files: Vec<String>, play: &str) -> Result<(), String> {
        self.record(format!("queue({queue_type:?},{},{play})", files.len()));
        Ok(())
    }
    fn browse_genres(&self, _offset: i32, _limit: i32) -> Result<Page<GenreData>, String> {
        self.record("browse_genres");
        Ok(self.browse_genres.clone())
    }
    fn browse_artists(
        &self,
        _offset: i32,
        _limit: i32,
        album_artists: bool,
    ) -> Result<Page<ArtistData>, String> {
        self.record(format!("browse_artists({album_artists})"));
        Ok(self.browse_artists.clone())
    }
    fn browse_albums(&self, _offset: i32, _limit: i32) -> Result<Page<AlbumData>, String> {
        self.record("browse_albums");
        Ok(self.browse_albums.clone())
    }
    fn browse_tracks(&self, _offset: i32, _limit: i32) -> Result<Page<Track>, String> {
        self.record("browse_tracks");
        Ok(self.browse_tracks.clone())
    }
    fn genre_artists(&self, genre: &str) -> Result<Vec<ArtistData>, String> {
        self.record(format!("genre_artists({genre})"));
        Ok(self.genre_artists.clone())
    }
    fn artist_albums(&self, artist: &str) -> Result<Vec<AlbumData>, String> {
        self.record(format!("artist_albums({artist})"));
        Ok(self.artist_albums.clone())
    }
    fn album_tracks(&self, album: &str) -> Result<Vec<Track>, String> {
        self.record(format!("album_tracks({album})"));
        Ok(self.album_tracks.clone())
    }
    fn album_cover(&self, artist: &str, album: &str, hash: &str) -> Result<AlbumCover, String> {
        self.record(format!("album_cover({artist},{album},{hash})"));
        Ok(self.album_cover.clone())
    }
    fn album_cover_page(&self, _offset: i32, _limit: i32) -> Result<Page<AlbumCoverItem>, String> {
        self.record("album_cover_page");
        Ok(self.album_cover_page.clone())
    }
    fn cover_cache_status(&self) -> Result<bool, String> {
        self.record("cover_cache_status");
        Ok(self.cover_cache_status)
    }
    fn album_identifiers(&self) -> Result<Vec<AlbumIdentifier>, String> {
        self.record("album_identifiers");
        Ok(self.album_identifiers.clone())
    }
    fn artwork_raw(&self, path: &str) -> Result<String, String> {
        self.record(format!("artwork_raw({path})"));
        Ok(self.artwork_raw.clone())
    }
    fn batch_metadata(&self, paths: Vec<String>) -> Result<Vec<TrackMetadata>, String> {
        self.record(format!("batch_metadata({})", paths.len()));
        Ok(self.batch_metadata.clone())
    }
    fn track_paths(&self) -> Result<Vec<String>, String> {
        self.record("track_paths");
        Ok(self.track_paths.clone())
    }
    fn tracks_for_paths(&self, paths: Vec<String>) -> Result<Vec<Track>, String> {
        self.record(format!("tracks_for_paths({})", paths.len()));
        Ok(self.tracks_for_paths.clone())
    }
    fn sync_delta(&self, updated_since: i64) -> Result<SyncDelta, String> {
        self.record(format!("sync_delta({updated_since})"));
        Ok(self.sync_delta.clone())
    }
    fn radio_stations(&self, _offset: i32, _limit: i32) -> Result<Page<RadioStation>, String> {
        self.record("radio_stations");
        Ok(self.radio_stations.clone())
    }
    fn play_all(&self, shuffle: bool) -> Result<(), String> {
        self.record(format!("play_all({shuffle})"));
        Ok(())
    }
    fn playlists(&self, _offset: i32, _limit: i32) -> Result<Page<Playlist>, String> {
        self.record("playlists");
        Ok(self.playlists.clone())
    }
    fn play_playlist(&self, url: &str) -> Result<(), String> {
        self.record(format!("play_playlist({url})"));
        Ok(())
    }
    fn plugin_version(&self) -> Result<String, String> {
        self.record("plugin_version");
        Ok(self.plugin_version.clone())
    }
    fn set_background_task_message(&self, message: &str) -> Result<(), String> {
        self.record(format!("set_background_task_message({message})"));
        Ok(())
    }
}
