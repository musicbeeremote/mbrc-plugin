//! The provider boundary: the trait every command handler depends on, so
//! handlers can be unit-tested with zero C ABI, zero MusicBee, zero sockets.
//!
//! Implementations:
//! - [`FfiProviders`] wraps [`SafeCallbacks`] and talks to the real C# shim.
//! - [`NullProviders`] is a benign no-op placeholder (returns defaults), usable
//!   wherever a real provider isn't needed (e.g. the handshake integration test).
//! - `MockProviders` (test-only) returns configurable canned data + records calls.
//!
//! Every call answers a [`HostResult`], so a client can be told whether MusicBee
//! declined, could not be reached, or failed.

use crate::ffi::callbacks::SafeCallbacks;
use crate::ffi::dtos::{
    AlbumCoverParams, ArtworkLocation, BatchMetadataParams, BrowseParams, FieldsParams,
    IndexParams, MoveParams, NowPlayingQueueParams, PaginationParams, PathParams, PathTags,
    PathsParams, PlaylistCreateParams, PlaylistFilesParams, PodcastEpisodeParams,
    PodcastEpisodesParams, QueryParams, SetBoolParams, SetIntParams, SetLfmRatingParams,
    SetRepeatParams, SourceParams, StringValueParams, SyncDeltaParams, TagChangeParams,
    TagWriteParams, TagWriteResult, TagsForPathsParams,
};
use crate::ffi::types::{CommandType, LibrarySource, QueryType};
use crate::protocol::messages::{
    AlbumCover, AlbumCoverItem, AlbumData, AlbumIdentifier, ArtistData, Cover, GenreData,
    LastfmStatus, Lyrics, NowPlayingListTrack, NowPlayingOrder, OutputDevices, Page,
    PlaybackPositionResponse, PlayerState, Playlist, PlaylistEntry, PlaylistFiles, PodcastEpisode,
    PodcastSubscription, QueueType, RadioStation, RepeatMode, SyncDelta, Track, TrackDetails,
    TrackInfo, TrackMetadata, TrackTags,
};

/// Why a call into MusicBee did not answer.
///
/// The kind decides what a V6 client is told; the detail is for the log, and
/// is what the error displays as.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum HostError {
    /// MusicBee declined: a command it answered `false` to, such as playing a
    /// playlist that is gone or tagging a file it cannot write.
    Refused(String),
    /// The core cannot reach MusicBee, because its callbacks are not registered.
    Unreachable(String),
    /// MusicBee or the plugin failed: it threw, or its answer did not decode.
    Failed(String),
}

/// What every [`Providers`] call answers.
pub type HostResult<T> = Result<T, HostError>;

impl HostError {
    pub fn detail(&self) -> &str {
        match self {
            Self::Refused(detail) | Self::Unreachable(detail) | Self::Failed(detail) => detail,
        }
    }
}

impl std::fmt::Display for HostError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(self.detail())
    }
}

impl std::error::Error for HostError {}

/// A failure the core describes in its own words.
impl From<String> for HostError {
    fn from(detail: String) -> Self {
        Self::Failed(detail)
    }
}

impl From<&str> for HostError {
    fn from(detail: &str) -> Self {
        Self::Failed(detail.to_owned())
    }
}

/// For callers that only report the error as text.
impl From<HostError> for String {
    fn from(e: HostError) -> Self {
        match e {
            HostError::Refused(detail)
            | HostError::Unreachable(detail)
            | HostError::Failed(detail) => detail,
        }
    }
}

/// The MusicBee data/command surface, as the core sees it. Handlers take
/// `&dyn Providers` (or `&impl Providers`) and never touch the raw FFI.
pub trait Providers: Send + Sync {
    // Player transport.
    fn play(&self) -> HostResult<()>;
    fn pause(&self) -> HostResult<()>;
    fn play_pause(&self) -> HostResult<()>;
    fn stop(&self) -> HostResult<()>;
    /// Closes MusicBee as its window's close button does.
    fn close_musicbee(&self) -> HostResult<()>;
    fn next(&self) -> HostResult<()>;
    fn previous(&self) -> HostResult<()>;
    fn set_volume(&self, volume: i32) -> HostResult<()>;
    fn set_position(&self, position_ms: i32) -> HostResult<()>;

    // Player state + modes.
    fn player_state(&self) -> HostResult<PlayerState>;
    fn set_mute(&self, value: bool) -> HostResult<()>;
    fn set_shuffle(&self, value: bool) -> HostResult<()>;
    /// Enable/disable AutoDJ. Used by the shuffle command's AutoDJ cycle
    /// (off -> shuffle -> autodj -> off), which all V4 clients negotiate.
    fn set_auto_dj(&self, value: bool) -> HostResult<()>;
    fn set_repeat(&self, mode: RepeatMode) -> HostResult<()>;
    fn set_scrobble(&self, value: bool) -> HostResult<()>;
    fn set_stop_after_current(&self, value: bool) -> HostResult<()>;

    // Output devices.
    fn output_devices(&self) -> HostResult<OutputDevices>;
    fn switch_output(&self, device: &str) -> HostResult<()>;

    // Now playing - track.
    fn playback_position(&self) -> HostResult<PlaybackPositionResponse>;
    fn track_info(&self) -> HostResult<TrackInfo>;
    fn track_details(&self) -> HostResult<TrackDetails>;
    fn cover(&self) -> HostResult<Cover>;
    fn lyrics(&self) -> HostResult<Lyrics>;
    fn rating(&self) -> HostResult<String>;
    fn set_rating(&self, value: &str) -> HostResult<()>;
    fn lfm_rating(&self) -> HostResult<LastfmStatus>;
    fn set_lfm_rating(&self, status: LastfmStatus) -> HostResult<()>;
    /// Whether a last.fm account is configured, so the core can reject scrobbling /
    /// love / ban with a proper `unavailable` error rather than a generic failure.
    fn has_lastfm_account(&self) -> HostResult<bool>;
    fn set_tag(&self, tag: &str, value: &str) -> HostResult<()>;
    /// Synchronized lyrics for the now-playing track (synced-preferred, #113). The
    /// raw text may carry LRC timestamps; the V6 codec parses them.
    fn now_playing_synced_lyrics(&self) -> HostResult<Lyrics>;

    // Now playing - list. `now_playing_list` is the sequential page (Android);
    // `now_playing_list_ordered` anchors at the current track and carries
    // album/album_artist (iOS). The handler picks by platform.
    fn now_playing_list(&self, offset: i32, limit: i32) -> HostResult<Page<NowPlayingListTrack>>;
    fn now_playing_list_ordered(
        &self,
        offset: i32,
        limit: i32,
    ) -> HostResult<Page<NowPlayingListTrack>>;
    /// Every path in the queue, in list order, without reading a tag.
    ///
    /// The page queries read tags for the window they serve, so neither can
    /// answer a question about the whole queue.
    fn now_playing_list_paths(&self) -> HostResult<Vec<String>>;
    /// The whole play order from the current track, without reading a tag.
    ///
    /// The ordered page reports the length of the window it served, so it cannot
    /// say how much is left to play.
    fn now_playing_list_order(&self) -> HostResult<NowPlayingOrder>;
    fn play_list_item(&self, index: i32) -> HostResult<()>;
    fn remove_list_item(&self, index: i32) -> HostResult<()>;
    fn move_list_item(&self, from: i32, to: i32) -> HostResult<()>;
    fn search_list(&self, query: &str) -> HostResult<()>;
    fn clear_list(&self) -> HostResult<()>;
    fn queue(&self, queue_type: QueueType, files: Vec<String>, play: &str) -> HostResult<()>;

    // Podcasts (#37). Read-mostly and event-free: MusicBee announces nothing
    // about them, so a client re-reads rather than waiting to be told. The
    // single-item reads answer with an empty list when nothing is there.
    fn podcast_subscriptions(
        &self,
        offset: i32,
        limit: i32,
    ) -> HostResult<Page<PodcastSubscription>>;
    fn podcast_subscription(&self, id: &str) -> HostResult<Vec<PodcastSubscription>>;
    fn podcast_episodes(
        &self,
        id: &str,
        offset: i32,
        limit: i32,
    ) -> HostResult<Page<PodcastEpisode>>;
    fn podcast_episode(&self, id: &str, index: i32) -> HostResult<Vec<PodcastEpisode>>;
    /// A subscription's artwork as raw bytes (base64), empty when it has none.
    fn podcast_artwork(&self, id: &str) -> HostResult<String>;
    /// The file MusicBee keeps its thumbnail of `artist` in, fetching it first
    /// when it has none cached. Empty, or an empty file, when MusicBee found none.
    fn artist_picture_thumb(&self, artist: &str) -> HostResult<String>;

    // Library - flat browse (paginated).
    fn browse_genres(&self, offset: i32, limit: i32) -> HostResult<Page<GenreData>>;
    fn browse_artists(
        &self,
        offset: i32,
        limit: i32,
        album_artists: bool,
    ) -> HostResult<Page<ArtistData>>;
    fn browse_albums(&self, offset: i32, limit: i32) -> HostResult<Page<AlbumData>>;
    fn browse_tracks(&self, offset: i32, limit: i32) -> HostResult<Page<Track>>;

    // Library - hierarchical navigation (iOS; non-paginated).
    fn genre_artists(&self, genre: &str) -> HostResult<Vec<ArtistData>>;
    fn artist_albums(&self, artist: &str) -> HostResult<Vec<AlbumData>>;
    fn album_tracks(&self, album: &str) -> HostResult<Vec<Track>>;
    /// Every track filed under one genre, which walking its albums cannot give.
    fn genre_tracks(&self, genre: &str) -> HostResult<Vec<Track>>;

    // Library - covers, radio, play-all.
    fn album_cover(&self, artist: &str, album: &str, hash: &str) -> HostResult<AlbumCover>;
    fn album_cover_page(&self, offset: i32, limit: i32) -> HostResult<Page<AlbumCoverItem>>;
    fn cover_cache_status(&self) -> HostResult<bool>;

    // Cover-cache leaves feeding the core's `CoverStore`, which owns resize,
    // hash and cache. `album_identifiers` folds one library scan into per-album
    // identities.
    fn album_identifiers(&self) -> HostResult<Vec<AlbumIdentifier>>;
    /// A track's artwork as the image bytes MusicBee holds; empty when it has none.
    fn artwork_raw(&self, path: &str) -> HostResult<Vec<u8>>;
    /// Where a track's artwork is, without its bytes.
    fn artwork_location(&self, path: &str) -> HostResult<ArtworkLocation>;
    fn batch_metadata(&self, paths: Vec<String>) -> HostResult<Vec<TrackMetadata>>;

    // Library cache. `track_paths` returns every track path in
    // browse order (the ordinal index, source of truth). `tracks_for_paths`
    // batch-reads the 7 browse tags for just a page's paths (bounded to O(page)).
    // `sync_delta` lists library changes since a watermark for the Scanner.
    fn track_paths(&self) -> HostResult<Vec<String>>;
    /// The music library tracks MusicBee's filtered queries leave out: banned ones.
    fn excluded_paths(&self) -> HostResult<Vec<String>>;
    /// Every file of one non-music category, in MusicBee's order.
    fn media_paths(&self, source: LibrarySource) -> HostResult<Vec<String>>;
    fn tracks_for_paths(&self, paths: Vec<String>) -> HostResult<Vec<Track>>;
    /// V6 typed tags per path: the base browse fields plus the raw extended tags
    /// (Year/Duration/Rating/DateAdded) the V6 `track` schema derives its typed
    /// fields from. Separate from `tracks_for_paths` (the thin V4 browse track).
    fn tracks_detailed_for_paths(&self, paths: Vec<String>) -> HostResult<Vec<TrackTags>>;
    fn sync_delta(&self, updated_since: i64) -> HostResult<SyncDelta>;

    /// The names the user gave MusicBee's fields, in the order asked.
    fn field_names(&self, fields: Vec<i32>) -> HostResult<Vec<String>>;
    /// The same fields of each path, raw as MusicBee holds them.
    fn tags_for_paths(&self, fields: Vec<i32>, paths: Vec<String>) -> HostResult<Vec<PathTags>>;
    /// The fields of the playing file, with its path.
    fn now_playing_tags(&self, fields: Vec<i32>) -> HostResult<PathTags>;
    /// Sets one field of the playing file, if `path` is still what plays,
    /// reading `read_field` before and after.
    fn write_now_playing_tag(
        &self,
        path: String,
        field: i32,
        read_field: i32,
        value: String,
    ) -> HostResult<TagWriteResult>;

    fn radio_stations(&self, offset: i32, limit: i32) -> HostResult<Page<RadioStation>>;
    fn play_all(&self, shuffle: bool) -> HostResult<()>;

    // Playlists.
    fn playlists(&self, offset: i32, limit: i32) -> HostResult<Page<Playlist>>;
    fn play_playlist(&self, url: &str) -> HostResult<()>;
    /// A playlist's files in playlist order, with its name.
    fn playlist_files(&self, url: &str) -> HostResult<PlaylistFiles>;
    /// Every playlist with its format, for V6.
    fn playlist_catalog(&self, offset: i32, limit: i32) -> HostResult<Page<PlaylistEntry>>;
    /// Creates a playlist holding `paths` and returns its url.
    fn create_playlist(&self, folder: &str, name: &str, paths: Vec<String>) -> HostResult<String>;
    fn delete_playlist(&self, url: &str) -> HostResult<()>;
    fn append_to_playlist(&self, url: &str, paths: Vec<String>) -> HostResult<()>;
    /// Replaces a playlist's contents with `paths`, in that order.
    fn set_playlist_files(&self, url: &str, paths: Vec<String>) -> HostResult<()>;

    // System.
    fn plugin_version(&self) -> HostResult<String>;
    /// Sets MusicBee's status-bar background-task message (host-only UI). Used to
    /// surface cover-cache build progress. Fire-and-forget.
    fn set_background_task_message(&self, message: &str) -> HostResult<()>;

    /// Pushes a core -> host UI event (fire-and-forget) so an open panel can
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

/// A MessagePack `bin` payload, read as bytes.
///
/// `Vec<u8>` alone deserializes from an array of integers and refuses `bin`,
/// which is what the host's `byte[]` becomes.
struct Bytes(Vec<u8>);

impl<'de> serde::Deserialize<'de> for Bytes {
    fn deserialize<D: serde::Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        struct Visitor;
        impl serde::de::Visitor<'_> for Visitor {
            type Value = Bytes;
            fn expecting(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
                f.write_str("a byte array")
            }
            fn visit_bytes<E: serde::de::Error>(self, bytes: &[u8]) -> Result<Bytes, E> {
                Ok(Bytes(bytes.to_vec()))
            }
            fn visit_byte_buf<E: serde::de::Error>(self, bytes: Vec<u8>) -> Result<Bytes, E> {
                Ok(Bytes(bytes))
            }
        }
        deserializer.deserialize_bytes(Visitor)
    }
}

impl Providers for FfiProviders {
    fn play(&self) -> HostResult<()> {
        self.callbacks.execute_command(CommandType::Play, &())
    }
    fn pause(&self) -> HostResult<()> {
        self.callbacks.execute_command(CommandType::Pause, &())
    }
    fn play_pause(&self) -> HostResult<()> {
        self.callbacks.execute_command(CommandType::PlayPause, &())
    }
    fn stop(&self) -> HostResult<()> {
        self.callbacks.execute_command(CommandType::Stop, &())
    }
    fn close_musicbee(&self) -> HostResult<()> {
        self.callbacks
            .execute_command(CommandType::CloseMusicBee, &())
    }
    fn next(&self) -> HostResult<()> {
        self.callbacks.execute_command(CommandType::Next, &())
    }
    fn previous(&self) -> HostResult<()> {
        self.callbacks.execute_command(CommandType::Previous, &())
    }
    fn set_volume(&self, volume: i32) -> HostResult<()> {
        self.callbacks
            .execute_command(CommandType::SetVolume, &SetIntParams { value: volume })
    }
    fn set_position(&self, position_ms: i32) -> HostResult<()> {
        self.callbacks.execute_command(
            CommandType::SetPosition,
            &SetIntParams { value: position_ms },
        )
    }

    fn player_state(&self) -> HostResult<PlayerState> {
        self.callbacks.query_no_params(QueryType::PlayerState)
    }
    fn set_mute(&self, value: bool) -> HostResult<()> {
        self.callbacks
            .execute_command(CommandType::SetMute, &SetBoolParams { value })
    }
    fn set_shuffle(&self, value: bool) -> HostResult<()> {
        self.callbacks
            .execute_command(CommandType::SetShuffle, &SetBoolParams { value })
    }
    fn set_auto_dj(&self, value: bool) -> HostResult<()> {
        self.callbacks
            .execute_command(CommandType::SetAutoDj, &SetBoolParams { value })
    }
    fn set_repeat(&self, mode: RepeatMode) -> HostResult<()> {
        self.callbacks
            .execute_command(CommandType::SetRepeat, &SetRepeatParams { mode })
    }
    fn set_scrobble(&self, value: bool) -> HostResult<()> {
        self.callbacks
            .execute_command(CommandType::SetScrobble, &SetBoolParams { value })
    }
    fn set_stop_after_current(&self, value: bool) -> HostResult<()> {
        self.callbacks
            .execute_command(CommandType::SetStopAfterCurrent, &SetBoolParams { value })
    }

    fn output_devices(&self) -> HostResult<OutputDevices> {
        self.callbacks.query_no_params(QueryType::OutputDevices)
    }
    fn switch_output(&self, device: &str) -> HostResult<()> {
        self.callbacks.execute_command(
            CommandType::OutputSwitch,
            &StringValueParams {
                value: device.to_string(),
            },
        )
    }

    fn playback_position(&self) -> HostResult<PlaybackPositionResponse> {
        self.callbacks.query_no_params(QueryType::PlaybackPosition)
    }
    fn track_info(&self) -> HostResult<TrackInfo> {
        self.callbacks.query_no_params(QueryType::TrackInfo)
    }
    fn track_details(&self) -> HostResult<TrackDetails> {
        self.callbacks.query_no_params(QueryType::NowPlayingDetails)
    }
    fn cover(&self) -> HostResult<Cover> {
        self.callbacks.query_no_params(QueryType::CoverData)
    }
    fn lyrics(&self) -> HostResult<Lyrics> {
        self.callbacks.query_no_params(QueryType::Lyrics)
    }
    fn now_playing_synced_lyrics(&self) -> HostResult<Lyrics> {
        self.callbacks
            .query_no_params(QueryType::NowPlayingLyricsSynced)
    }
    fn rating(&self) -> HostResult<String> {
        self.callbacks.query_no_params(QueryType::NowPlayingRating)
    }
    fn set_rating(&self, value: &str) -> HostResult<()> {
        self.callbacks.execute_command(
            CommandType::SetRating,
            &StringValueParams {
                value: value.to_string(),
            },
        )
    }
    fn lfm_rating(&self) -> HostResult<LastfmStatus> {
        self.callbacks
            .query_no_params(QueryType::NowPlayingLfmRating)
    }
    fn set_lfm_rating(&self, status: LastfmStatus) -> HostResult<()> {
        self.callbacks
            .execute_command(CommandType::SetLfmRating, &SetLfmRatingParams { status })
    }
    fn has_lastfm_account(&self) -> HostResult<bool> {
        self.callbacks.query_no_params(QueryType::HasLastFmAccount)
    }
    fn set_tag(&self, tag: &str, value: &str) -> HostResult<()> {
        self.callbacks.execute_command(
            CommandType::NowPlayingTagChange,
            &TagChangeParams {
                tag: tag.to_string(),
                value: value.to_string(),
            },
        )
    }

    fn now_playing_list(&self, offset: i32, limit: i32) -> HostResult<Page<NowPlayingListTrack>> {
        self.callbacks.query(
            QueryType::NowPlayingList,
            &PaginationParams { offset, limit },
        )
    }
    fn now_playing_list_ordered(
        &self,
        offset: i32,
        limit: i32,
    ) -> HostResult<Page<NowPlayingListTrack>> {
        self.callbacks.query(
            QueryType::NowPlayingListOrdered,
            &PaginationParams { offset, limit },
        )
    }
    fn now_playing_list_paths(&self) -> HostResult<Vec<String>> {
        self.callbacks
            .query_no_params(QueryType::NowPlayingListPaths)
    }
    fn now_playing_list_order(&self) -> HostResult<NowPlayingOrder> {
        self.callbacks
            .query_no_params(QueryType::NowPlayingListOrder)
    }
    fn podcast_subscriptions(
        &self,
        offset: i32,
        limit: i32,
    ) -> HostResult<Page<PodcastSubscription>> {
        self.callbacks.query(
            QueryType::PodcastSubscriptions,
            &PaginationParams { offset, limit },
        )
    }
    fn podcast_subscription(&self, id: &str) -> HostResult<Vec<PodcastSubscription>> {
        self.callbacks.query(
            QueryType::PodcastSubscription,
            &QueryParams {
                query: id.to_string(),
            },
        )
    }
    fn podcast_episodes(
        &self,
        id: &str,
        offset: i32,
        limit: i32,
    ) -> HostResult<Page<PodcastEpisode>> {
        self.callbacks.query(
            QueryType::PodcastEpisodes,
            &PodcastEpisodesParams {
                id: id.to_string(),
                offset,
                limit,
            },
        )
    }
    fn podcast_episode(&self, id: &str, index: i32) -> HostResult<Vec<PodcastEpisode>> {
        self.callbacks.query(
            QueryType::PodcastEpisode,
            &PodcastEpisodeParams {
                id: id.to_string(),
                index,
            },
        )
    }
    fn podcast_artwork(&self, id: &str) -> HostResult<String> {
        self.callbacks.query(
            QueryType::PodcastArtwork,
            &QueryParams {
                query: id.to_string(),
            },
        )
    }
    fn artist_picture_thumb(&self, artist: &str) -> HostResult<String> {
        self.callbacks.query(
            QueryType::ArtistPictureThumb,
            &QueryParams {
                query: artist.to_string(),
            },
        )
    }
    fn play_list_item(&self, index: i32) -> HostResult<()> {
        self.callbacks
            .execute_command(CommandType::NowPlayingListPlay, &IndexParams { index })
    }
    fn remove_list_item(&self, index: i32) -> HostResult<()> {
        self.callbacks
            .execute_command(CommandType::NowPlayingListRemove, &IndexParams { index })
    }
    fn move_list_item(&self, from: i32, to: i32) -> HostResult<()> {
        self.callbacks
            .execute_command(CommandType::NowPlayingListMove, &MoveParams { from, to })
    }
    fn clear_list(&self) -> HostResult<()> {
        self.callbacks
            .execute_command(CommandType::NowPlayingListClear, &())
    }
    fn search_list(&self, query: &str) -> HostResult<()> {
        self.callbacks.execute_command(
            CommandType::NowPlayingListSearch,
            &StringValueParams {
                value: query.to_string(),
            },
        )
    }
    fn queue(&self, queue_type: QueueType, files: Vec<String>, play: &str) -> HostResult<()> {
        self.callbacks.execute_command(
            CommandType::NowPlayingQueue,
            &NowPlayingQueueParams {
                queue_type,
                files,
                play: play.to_string(),
            },
        )
    }

    fn browse_genres(&self, offset: i32, limit: i32) -> HostResult<Page<GenreData>> {
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
    ) -> HostResult<Page<ArtistData>> {
        self.callbacks.query(
            QueryType::LibraryBrowseArtists,
            &BrowseParams {
                offset,
                limit,
                album_artists,
            },
        )
    }
    fn browse_albums(&self, offset: i32, limit: i32) -> HostResult<Page<AlbumData>> {
        self.callbacks.query(
            QueryType::LibraryBrowseAlbums,
            &PaginationParams { offset, limit },
        )
    }
    fn browse_tracks(&self, offset: i32, limit: i32) -> HostResult<Page<Track>> {
        self.callbacks.query(
            QueryType::LibraryBrowseTracks,
            &PaginationParams { offset, limit },
        )
    }
    fn genre_artists(&self, genre: &str) -> HostResult<Vec<ArtistData>> {
        self.callbacks.query(
            QueryType::LibraryGenreArtists,
            &QueryParams {
                query: genre.to_string(),
            },
        )
    }
    fn artist_albums(&self, artist: &str) -> HostResult<Vec<AlbumData>> {
        self.callbacks.query(
            QueryType::LibraryArtistAlbums,
            &QueryParams {
                query: artist.to_string(),
            },
        )
    }
    fn album_tracks(&self, album: &str) -> HostResult<Vec<Track>> {
        self.callbacks.query(
            QueryType::LibraryAlbumTracks,
            &QueryParams {
                query: album.to_string(),
            },
        )
    }
    fn genre_tracks(&self, genre: &str) -> HostResult<Vec<Track>> {
        self.callbacks.query(
            QueryType::LibraryGenreTracks,
            &QueryParams {
                query: genre.to_string(),
            },
        )
    }
    fn album_cover(&self, artist: &str, album: &str, hash: &str) -> HostResult<AlbumCover> {
        self.callbacks.query(
            QueryType::AlbumCover,
            &AlbumCoverParams {
                artist: artist.to_string(),
                album: album.to_string(),
                client_hash: hash.to_string(),
            },
        )
    }
    fn album_cover_page(&self, offset: i32, limit: i32) -> HostResult<Page<AlbumCoverItem>> {
        self.callbacks.query(
            QueryType::AlbumCoverBatch,
            &PaginationParams { offset, limit },
        )
    }
    fn cover_cache_status(&self) -> HostResult<bool> {
        self.callbacks
            .query_no_params(QueryType::CoverCacheBuildStatus)
    }
    fn album_identifiers(&self) -> HostResult<Vec<AlbumIdentifier>> {
        self.callbacks.query_no_params(QueryType::AlbumIdentifiers)
    }
    fn artwork_location(&self, path: &str) -> HostResult<ArtworkLocation> {
        self.callbacks.query(
            QueryType::ArtworkLocationForPath,
            &PathParams {
                path: path.to_string(),
            },
        )
    }
    fn artwork_raw(&self, path: &str) -> HostResult<Vec<u8>> {
        self.callbacks
            .query::<_, Bytes>(
                QueryType::ArtworkRawForPath,
                &PathParams {
                    path: path.to_string(),
                },
            )
            .map(|bytes| bytes.0)
    }
    fn batch_metadata(&self, paths: Vec<String>) -> HostResult<Vec<TrackMetadata>> {
        self.callbacks
            .query(QueryType::BatchMetadata, &BatchMetadataParams { paths })
    }
    fn track_paths(&self) -> HostResult<Vec<String>> {
        self.callbacks.query_no_params(QueryType::LibraryTrackPaths)
    }
    fn excluded_paths(&self) -> HostResult<Vec<String>> {
        self.callbacks
            .query_no_params(QueryType::LibraryExcludedPaths)
    }
    fn media_paths(&self, source: LibrarySource) -> HostResult<Vec<String>> {
        self.callbacks.query(
            QueryType::MediaPaths,
            &SourceParams {
                source: source as i32,
            },
        )
    }
    fn tracks_for_paths(&self, paths: Vec<String>) -> HostResult<Vec<Track>> {
        self.callbacks
            .query(QueryType::LibraryTracksForPaths, &PathsParams { paths })
    }
    fn tracks_detailed_for_paths(&self, paths: Vec<String>) -> HostResult<Vec<TrackTags>> {
        self.callbacks
            .query(QueryType::LibraryTrackTags, &PathsParams { paths })
    }
    fn field_names(&self, fields: Vec<i32>) -> HostResult<Vec<String>> {
        self.callbacks
            .query(QueryType::TagFieldNames, &FieldsParams { fields })
    }
    fn tags_for_paths(&self, fields: Vec<i32>, paths: Vec<String>) -> HostResult<Vec<PathTags>> {
        self.callbacks.query(
            QueryType::TagsForPaths,
            &TagsForPathsParams { fields, paths },
        )
    }
    fn now_playing_tags(&self, fields: Vec<i32>) -> HostResult<PathTags> {
        self.callbacks
            .query(QueryType::NowPlayingTags, &FieldsParams { fields })
    }
    fn write_now_playing_tag(
        &self,
        path: String,
        field: i32,
        read_field: i32,
        value: String,
    ) -> HostResult<TagWriteResult> {
        self.callbacks.query(
            QueryType::NowPlayingTagWrite,
            &TagWriteParams {
                path,
                field,
                read_field,
                value,
            },
        )
    }
    fn sync_delta(&self, updated_since: i64) -> HostResult<SyncDelta> {
        self.callbacks.query(
            QueryType::LibrarySyncDelta,
            &SyncDeltaParams { updated_since },
        )
    }
    fn radio_stations(&self, offset: i32, limit: i32) -> HostResult<Page<RadioStation>> {
        self.callbacks.query(
            QueryType::RadioStations,
            &PaginationParams { offset, limit },
        )
    }
    fn play_all(&self, shuffle: bool) -> HostResult<()> {
        self.callbacks.execute_command(
            CommandType::LibraryPlayAll,
            &SetBoolParams { value: shuffle },
        )
    }
    fn playlists(&self, offset: i32, limit: i32) -> HostResult<Page<Playlist>> {
        self.callbacks
            .query(QueryType::PlaylistList, &PaginationParams { offset, limit })
    }
    fn playlist_files(&self, url: &str) -> HostResult<PlaylistFiles> {
        self.callbacks.query(
            QueryType::PlaylistTracks,
            &QueryParams {
                query: url.to_string(),
            },
        )
    }
    fn playlist_catalog(&self, offset: i32, limit: i32) -> HostResult<Page<PlaylistEntry>> {
        self.callbacks.query(
            QueryType::PlaylistCatalog,
            &PaginationParams { offset, limit },
        )
    }
    fn create_playlist(&self, folder: &str, name: &str, paths: Vec<String>) -> HostResult<String> {
        let url: String = self.callbacks.query(
            QueryType::PlaylistCreate,
            &PlaylistCreateParams {
                folder: folder.to_string(),
                name: name.to_string(),
                files: paths,
            },
        )?;
        if url.is_empty() {
            return Err("MusicBee did not create the playlist".into());
        }
        Ok(url)
    }
    fn delete_playlist(&self, url: &str) -> HostResult<()> {
        self.callbacks.execute_command(
            CommandType::PlaylistDelete,
            &StringValueParams {
                value: url.to_string(),
            },
        )
    }
    fn append_to_playlist(&self, url: &str, paths: Vec<String>) -> HostResult<()> {
        self.callbacks.execute_command(
            CommandType::PlaylistAppend,
            &PlaylistFilesParams {
                url: url.to_string(),
                files: paths,
            },
        )
    }
    fn set_playlist_files(&self, url: &str, paths: Vec<String>) -> HostResult<()> {
        self.callbacks.execute_command(
            CommandType::PlaylistSetFiles,
            &PlaylistFilesParams {
                url: url.to_string(),
                files: paths,
            },
        )
    }
    fn play_playlist(&self, url: &str) -> HostResult<()> {
        self.callbacks.execute_command(
            CommandType::PlaylistPlay,
            &StringValueParams {
                value: url.to_string(),
            },
        )
    }
    fn plugin_version(&self) -> HostResult<String> {
        self.callbacks.query_no_params(QueryType::PluginVersion)
    }
    fn set_background_task_message(&self, message: &str) -> HostResult<()> {
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

/// A no-op provider: queries return defaults, commands succeed.
///
/// A benign placeholder for contexts that don't drive MusicBee (handshake-only
/// tests, future stand-ins). Grows with the trait so callers don't have to.
pub struct NullProviders;

impl Providers for NullProviders {
    fn play(&self) -> HostResult<()> {
        Ok(())
    }
    fn pause(&self) -> HostResult<()> {
        Ok(())
    }
    fn play_pause(&self) -> HostResult<()> {
        Ok(())
    }
    fn stop(&self) -> HostResult<()> {
        Ok(())
    }
    fn close_musicbee(&self) -> HostResult<()> {
        Ok(())
    }
    fn next(&self) -> HostResult<()> {
        Ok(())
    }
    fn previous(&self) -> HostResult<()> {
        Ok(())
    }
    fn set_volume(&self, _volume: i32) -> HostResult<()> {
        Ok(())
    }
    fn set_position(&self, _position_ms: i32) -> HostResult<()> {
        Ok(())
    }
    fn player_state(&self) -> HostResult<PlayerState> {
        Ok(PlayerState::default())
    }
    fn set_mute(&self, _value: bool) -> HostResult<()> {
        Ok(())
    }
    fn set_shuffle(&self, _value: bool) -> HostResult<()> {
        Ok(())
    }
    fn set_auto_dj(&self, _value: bool) -> HostResult<()> {
        Ok(())
    }
    fn set_repeat(&self, _mode: RepeatMode) -> HostResult<()> {
        Ok(())
    }
    fn set_scrobble(&self, _value: bool) -> HostResult<()> {
        Ok(())
    }
    fn set_stop_after_current(&self, _value: bool) -> HostResult<()> {
        Ok(())
    }
    fn output_devices(&self) -> HostResult<OutputDevices> {
        Ok(OutputDevices::default())
    }
    fn switch_output(&self, _device: &str) -> HostResult<()> {
        Ok(())
    }
    fn playback_position(&self) -> HostResult<PlaybackPositionResponse> {
        Ok(PlaybackPositionResponse::default())
    }
    fn track_info(&self) -> HostResult<TrackInfo> {
        Ok(TrackInfo::default())
    }
    fn track_details(&self) -> HostResult<TrackDetails> {
        Ok(TrackDetails::default())
    }
    fn cover(&self) -> HostResult<Cover> {
        Ok(Cover::default())
    }
    fn lyrics(&self) -> HostResult<Lyrics> {
        Ok(Lyrics::default())
    }
    fn now_playing_synced_lyrics(&self) -> HostResult<Lyrics> {
        Ok(Lyrics::default())
    }
    fn rating(&self) -> HostResult<String> {
        Ok(String::new())
    }
    fn set_rating(&self, _value: &str) -> HostResult<()> {
        Ok(())
    }
    fn lfm_rating(&self) -> HostResult<LastfmStatus> {
        Ok(LastfmStatus::default())
    }
    fn set_lfm_rating(&self, _status: LastfmStatus) -> HostResult<()> {
        Ok(())
    }
    fn has_lastfm_account(&self) -> HostResult<bool> {
        Ok(true)
    }
    fn set_tag(&self, _tag: &str, _value: &str) -> HostResult<()> {
        Ok(())
    }
    fn now_playing_list(&self, _offset: i32, _limit: i32) -> HostResult<Page<NowPlayingListTrack>> {
        Ok(Page::default())
    }
    fn now_playing_list_ordered(
        &self,
        _offset: i32,
        _limit: i32,
    ) -> HostResult<Page<NowPlayingListTrack>> {
        Ok(Page::default())
    }
    fn now_playing_list_paths(&self) -> HostResult<Vec<String>> {
        Ok(Vec::new())
    }
    fn now_playing_list_order(&self) -> HostResult<NowPlayingOrder> {
        Ok(NowPlayingOrder::default())
    }
    fn podcast_subscriptions(
        &self,
        _offset: i32,
        _limit: i32,
    ) -> HostResult<Page<PodcastSubscription>> {
        Ok(Page::default())
    }
    fn podcast_subscription(&self, _id: &str) -> HostResult<Vec<PodcastSubscription>> {
        Ok(Vec::new())
    }
    fn podcast_episodes(
        &self,
        _id: &str,
        _offset: i32,
        _limit: i32,
    ) -> HostResult<Page<PodcastEpisode>> {
        Ok(Page::default())
    }
    fn podcast_episode(&self, _id: &str, _index: i32) -> HostResult<Vec<PodcastEpisode>> {
        Ok(Vec::new())
    }
    fn podcast_artwork(&self, _id: &str) -> HostResult<String> {
        Ok(String::new())
    }
    fn artist_picture_thumb(&self, _artist: &str) -> HostResult<String> {
        Ok(String::new())
    }
    fn play_list_item(&self, _index: i32) -> HostResult<()> {
        Ok(())
    }
    fn remove_list_item(&self, _index: i32) -> HostResult<()> {
        Ok(())
    }
    fn move_list_item(&self, _from: i32, _to: i32) -> HostResult<()> {
        Ok(())
    }
    fn search_list(&self, _query: &str) -> HostResult<()> {
        Ok(())
    }
    fn clear_list(&self) -> HostResult<()> {
        Ok(())
    }
    fn queue(&self, _queue_type: QueueType, _files: Vec<String>, _play: &str) -> HostResult<()> {
        Ok(())
    }
    fn browse_genres(&self, _offset: i32, _limit: i32) -> HostResult<Page<GenreData>> {
        Ok(Page::default())
    }
    fn browse_artists(
        &self,
        _offset: i32,
        _limit: i32,
        _album_artists: bool,
    ) -> HostResult<Page<ArtistData>> {
        Ok(Page::default())
    }
    fn browse_albums(&self, _offset: i32, _limit: i32) -> HostResult<Page<AlbumData>> {
        Ok(Page::default())
    }
    fn browse_tracks(&self, _offset: i32, _limit: i32) -> HostResult<Page<Track>> {
        Ok(Page::default())
    }
    fn genre_artists(&self, _genre: &str) -> HostResult<Vec<ArtistData>> {
        Ok(Vec::new())
    }
    fn artist_albums(&self, _artist: &str) -> HostResult<Vec<AlbumData>> {
        Ok(Vec::new())
    }
    fn album_tracks(&self, _album: &str) -> HostResult<Vec<Track>> {
        Ok(Vec::new())
    }
    fn genre_tracks(&self, _genre: &str) -> HostResult<Vec<Track>> {
        Ok(Vec::new())
    }
    fn album_cover(&self, _artist: &str, _album: &str, _hash: &str) -> HostResult<AlbumCover> {
        Ok(AlbumCover::default())
    }
    fn album_cover_page(&self, _offset: i32, _limit: i32) -> HostResult<Page<AlbumCoverItem>> {
        Ok(Page::default())
    }
    fn cover_cache_status(&self) -> HostResult<bool> {
        Ok(false)
    }
    fn album_identifiers(&self) -> HostResult<Vec<AlbumIdentifier>> {
        Ok(Vec::new())
    }
    fn artwork_location(&self, _path: &str) -> HostResult<ArtworkLocation> {
        Ok(ArtworkLocation::default())
    }
    fn artwork_raw(&self, _path: &str) -> HostResult<Vec<u8>> {
        Ok(Vec::new())
    }
    fn batch_metadata(&self, _paths: Vec<String>) -> HostResult<Vec<TrackMetadata>> {
        Ok(Vec::new())
    }
    fn track_paths(&self) -> HostResult<Vec<String>> {
        Ok(Vec::new())
    }
    fn excluded_paths(&self) -> HostResult<Vec<String>> {
        Ok(Vec::new())
    }
    fn media_paths(&self, _source: LibrarySource) -> HostResult<Vec<String>> {
        Ok(Vec::new())
    }
    fn tracks_for_paths(&self, _paths: Vec<String>) -> HostResult<Vec<Track>> {
        Ok(Vec::new())
    }
    fn tracks_detailed_for_paths(&self, _paths: Vec<String>) -> HostResult<Vec<TrackTags>> {
        Ok(Vec::new())
    }
    fn field_names(&self, fields: Vec<i32>) -> HostResult<Vec<String>> {
        Ok(vec![String::new(); fields.len()])
    }
    fn tags_for_paths(&self, _fields: Vec<i32>, _paths: Vec<String>) -> HostResult<Vec<PathTags>> {
        Ok(Vec::new())
    }
    fn now_playing_tags(&self, _fields: Vec<i32>) -> HostResult<PathTags> {
        Ok(PathTags::default())
    }
    fn write_now_playing_tag(
        &self,
        _path: String,
        _field: i32,
        _read_field: i32,
        _value: String,
    ) -> HostResult<TagWriteResult> {
        Ok(TagWriteResult {
            outcome: "failed".into(),
            reason: "no host".into(),
            ..TagWriteResult::default()
        })
    }
    fn sync_delta(&self, _updated_since: i64) -> HostResult<SyncDelta> {
        Ok(SyncDelta::default())
    }
    fn radio_stations(&self, _offset: i32, _limit: i32) -> HostResult<Page<RadioStation>> {
        Ok(Page::default())
    }
    fn play_all(&self, _shuffle: bool) -> HostResult<()> {
        Ok(())
    }
    fn playlists(&self, _offset: i32, _limit: i32) -> HostResult<Page<Playlist>> {
        Ok(Page::default())
    }
    fn play_playlist(&self, _url: &str) -> HostResult<()> {
        Ok(())
    }
    fn playlist_files(&self, _url: &str) -> HostResult<PlaylistFiles> {
        Ok(PlaylistFiles::default())
    }
    fn playlist_catalog(&self, _offset: i32, _limit: i32) -> HostResult<Page<PlaylistEntry>> {
        Ok(Page::default())
    }
    fn create_playlist(
        &self,
        _folder: &str,
        _name: &str,
        _paths: Vec<String>,
    ) -> HostResult<String> {
        Err("no host".into())
    }
    fn delete_playlist(&self, _url: &str) -> HostResult<()> {
        Ok(())
    }
    fn append_to_playlist(&self, _url: &str, _paths: Vec<String>) -> HostResult<()> {
        Ok(())
    }
    fn set_playlist_files(&self, _url: &str, _paths: Vec<String>) -> HostResult<()> {
        Ok(())
    }
    fn plugin_version(&self) -> HostResult<String> {
        Ok(String::new())
    }
    fn set_background_task_message(&self, _message: &str) -> HostResult<()> {
        Ok(())
    }
}

/// Test double: returns configurable canned data and records the calls made.
#[cfg(test)]
#[derive(Default)]
pub struct MockProviders {
    /// How long `output_devices` takes, standing in for a slow host call.
    pub output_devices_stall: Option<std::time::Duration>,
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
    pub now_playing_list_paths: Vec<String>,
    pub now_playing_list_order: NowPlayingOrder,
    /// The storage index whose removal the host refuses, if any.
    pub refuse_remove_at: Option<i32>,
    pub podcast_subscriptions: Page<PodcastSubscription>,
    pub podcast_episodes: Page<PodcastEpisode>,
    pub podcast_artwork: String,
    /// Artist name -> the thumbnail path `artist_picture_thumb` answers.
    pub artist_thumbs: std::collections::HashMap<String, String>,
    /// Makes `artist_picture_thumb` take this long, as MusicBee does when it downloads.
    pub artist_thumb_stall: Option<std::time::Duration>,
    pub browse_genres: Page<GenreData>,
    pub browse_artists: Page<ArtistData>,
    pub browse_albums: Page<AlbumData>,
    pub browse_tracks: Page<Track>,
    pub genre_artists: Vec<ArtistData>,
    pub artist_albums: Vec<AlbumData>,
    pub album_tracks: Vec<Track>,
    pub genre_tracks: Vec<Track>,
    pub album_cover: AlbumCover,
    pub album_cover_page: Page<AlbumCoverItem>,
    pub cover_cache_status: bool,
    pub has_lastfm_account: bool,
    pub album_identifiers: Vec<AlbumIdentifier>,
    pub artwork_raw: Vec<u8>,
    pub batch_metadata: Vec<TrackMetadata>,
    pub track_paths: Vec<String>,
    pub excluded_paths: Vec<String>,
    /// Each category's listing, keyed by its `LibrarySource` flag.
    pub media_paths: std::sync::Mutex<std::collections::HashMap<i32, Vec<String>>>,
    pub tracks_for_paths: Vec<Track>,
    pub tracks_detailed: Vec<TrackTags>,
    pub sync_delta: SyncDelta,
    pub radio_stations: Page<RadioStation>,
    pub playlists: Page<Playlist>,
    pub playlist_files: PlaylistFiles,
    pub playlist_catalog: Page<PlaylistEntry>,
    /// What the last playlist write left, read back in place of
    /// `playlist_files` once set, as the host would.
    pub written_playlist: std::sync::Mutex<Option<Vec<String>>>,
    /// Refuse every playlist write, as a host would for a read-only file.
    pub refuse_playlist_writes: bool,
    pub plugin_version: String,
    pub calls: std::sync::Mutex<Vec<String>>,
    /// The user's field names, by MusicBee field id.
    pub field_names: std::collections::HashMap<i32, String>,
    /// Fields whose writes MusicBee accepts and does not keep, the way a custom
    /// field with no tag set up behaves: the field keeps its previous value.
    pub dropped_fields: Vec<i32>,
    /// Every file's field values by MusicBee field id; tag writes land here.
    pub file_tags:
        std::sync::Mutex<std::collections::HashMap<String, std::collections::HashMap<i32, String>>>,
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
    fn play(&self) -> HostResult<()> {
        self.record("play");
        Ok(())
    }
    fn pause(&self) -> HostResult<()> {
        self.record("pause");
        Ok(())
    }
    fn play_pause(&self) -> HostResult<()> {
        self.record("play_pause");
        Ok(())
    }
    fn stop(&self) -> HostResult<()> {
        self.record("stop");
        Ok(())
    }
    fn close_musicbee(&self) -> HostResult<()> {
        self.record("close_musicbee");
        Ok(())
    }
    fn next(&self) -> HostResult<()> {
        self.record("next");
        Ok(())
    }
    fn previous(&self) -> HostResult<()> {
        self.record("previous");
        Ok(())
    }
    fn set_volume(&self, volume: i32) -> HostResult<()> {
        self.record(format!("set_volume({volume})"));
        Ok(())
    }
    fn set_position(&self, position_ms: i32) -> HostResult<()> {
        self.record(format!("set_position({position_ms})"));
        Ok(())
    }
    fn player_state(&self) -> HostResult<PlayerState> {
        self.record("player_state");
        Ok(self.player_state.clone())
    }
    fn set_mute(&self, value: bool) -> HostResult<()> {
        self.record(format!("set_mute({value})"));
        Ok(())
    }
    fn set_shuffle(&self, value: bool) -> HostResult<()> {
        self.record(format!("set_shuffle({value})"));
        Ok(())
    }
    fn set_auto_dj(&self, value: bool) -> HostResult<()> {
        self.record(format!("set_auto_dj({value})"));
        Ok(())
    }
    fn set_repeat(&self, mode: RepeatMode) -> HostResult<()> {
        self.record(format!("set_repeat({mode:?})"));
        Ok(())
    }
    fn set_stop_after_current(&self, value: bool) -> HostResult<()> {
        self.record(format!("set_stop_after_current({value})"));
        Ok(())
    }
    fn set_scrobble(&self, value: bool) -> HostResult<()> {
        self.record(format!("set_scrobble({value})"));
        Ok(())
    }
    fn output_devices(&self) -> HostResult<OutputDevices> {
        self.record("output_devices");
        if let Some(stall) = self.output_devices_stall {
            std::thread::sleep(stall);
        }
        Ok(self.output_devices.clone())
    }
    fn switch_output(&self, device: &str) -> HostResult<()> {
        self.record(format!("switch_output({device})"));
        Ok(())
    }
    fn playback_position(&self) -> HostResult<PlaybackPositionResponse> {
        self.record("playback_position");
        Ok(self.position.clone())
    }
    fn track_info(&self) -> HostResult<TrackInfo> {
        self.record("track_info");
        Ok(self.track_info.clone())
    }
    fn track_details(&self) -> HostResult<TrackDetails> {
        self.record("track_details");
        Ok(self.track_details.clone())
    }
    fn cover(&self) -> HostResult<Cover> {
        self.record("cover");
        Ok(self.cover.clone())
    }
    fn lyrics(&self) -> HostResult<Lyrics> {
        self.record("lyrics");
        Ok(self.lyrics.clone())
    }
    fn now_playing_synced_lyrics(&self) -> HostResult<Lyrics> {
        self.record("now_playing_synced_lyrics");
        Ok(self.lyrics.clone())
    }
    fn rating(&self) -> HostResult<String> {
        self.record("rating");
        Ok(self.rating.clone())
    }
    fn set_rating(&self, value: &str) -> HostResult<()> {
        self.record(format!("set_rating({value})"));
        Ok(())
    }
    fn lfm_rating(&self) -> HostResult<LastfmStatus> {
        self.record("lfm_rating");
        Ok(self.lfm_rating)
    }
    fn set_lfm_rating(&self, status: LastfmStatus) -> HostResult<()> {
        self.record(format!("set_lfm_rating({status:?})"));
        Ok(())
    }
    fn has_lastfm_account(&self) -> HostResult<bool> {
        self.record("has_lastfm_account");
        Ok(self.has_lastfm_account)
    }
    fn set_tag(&self, tag: &str, value: &str) -> HostResult<()> {
        self.record(format!("set_tag({tag},{value})"));
        Ok(())
    }
    fn now_playing_list(&self, _offset: i32, _limit: i32) -> HostResult<Page<NowPlayingListTrack>> {
        self.record("now_playing_list");
        Ok(self.now_playing_list.clone())
    }
    fn now_playing_list_ordered(
        &self,
        _offset: i32,
        _limit: i32,
    ) -> HostResult<Page<NowPlayingListTrack>> {
        self.record("now_playing_list_ordered");
        Ok(self.now_playing_list_ordered.clone())
    }
    fn now_playing_list_paths(&self) -> HostResult<Vec<String>> {
        self.record("now_playing_list_paths");
        Ok(self.now_playing_list_paths.clone())
    }
    fn now_playing_list_order(&self) -> HostResult<NowPlayingOrder> {
        self.record("now_playing_list_order");
        Ok(self.now_playing_list_order.clone())
    }
    fn podcast_subscriptions(
        &self,
        _offset: i32,
        _limit: i32,
    ) -> HostResult<Page<PodcastSubscription>> {
        self.record("podcast_subscriptions");
        Ok(self.podcast_subscriptions.clone())
    }
    fn podcast_subscription(&self, id: &str) -> HostResult<Vec<PodcastSubscription>> {
        self.record(format!("podcast_subscription({id})"));
        Ok(self
            .podcast_subscriptions
            .data
            .iter()
            .filter(|s| s.id == id)
            .cloned()
            .collect())
    }
    fn podcast_episodes(
        &self,
        id: &str,
        _offset: i32,
        _limit: i32,
    ) -> HostResult<Page<PodcastEpisode>> {
        self.record(format!("podcast_episodes({id})"));
        Ok(self.podcast_episodes.clone())
    }
    fn podcast_episode(&self, id: &str, index: i32) -> HostResult<Vec<PodcastEpisode>> {
        self.record(format!("podcast_episode({id},{index})"));
        Ok(self
            .podcast_episodes
            .data
            .iter()
            .filter(|e| e.index == index)
            .cloned()
            .collect())
    }
    fn podcast_artwork(&self, id: &str) -> HostResult<String> {
        self.record(format!("podcast_artwork({id})"));
        Ok(self.podcast_artwork.clone())
    }
    fn artist_picture_thumb(&self, artist: &str) -> HostResult<String> {
        self.record(format!("artist_picture_thumb({artist})"));
        if let Some(stall) = self.artist_thumb_stall {
            std::thread::sleep(stall);
        }
        Ok(self.artist_thumbs.get(artist).cloned().unwrap_or_default())
    }
    fn play_list_item(&self, index: i32) -> HostResult<()> {
        self.record(format!("play_list_item({index})"));
        Ok(())
    }
    fn remove_list_item(&self, index: i32) -> HostResult<()> {
        if self.refuse_remove_at == Some(index) {
            return Err(HostError::Refused(format!(
                "host refused to remove {index}"
            )));
        }
        self.record(format!("remove_list_item({index})"));
        Ok(())
    }
    fn move_list_item(&self, from: i32, to: i32) -> HostResult<()> {
        self.record(format!("move_list_item({from},{to})"));
        Ok(())
    }
    fn search_list(&self, query: &str) -> HostResult<()> {
        self.record(format!("search_list({query})"));
        Ok(())
    }
    fn clear_list(&self) -> HostResult<()> {
        self.record("clear_list()".to_owned());
        Ok(())
    }
    fn queue(&self, queue_type: QueueType, files: Vec<String>, play: &str) -> HostResult<()> {
        self.record(format!(
            "queue({queue_type:?},[{}],{play})",
            files.join(",")
        ));
        Ok(())
    }
    fn browse_genres(&self, _offset: i32, _limit: i32) -> HostResult<Page<GenreData>> {
        self.record("browse_genres");
        Ok(self.browse_genres.clone())
    }
    fn browse_artists(
        &self,
        _offset: i32,
        _limit: i32,
        album_artists: bool,
    ) -> HostResult<Page<ArtistData>> {
        self.record(format!("browse_artists({album_artists})"));
        Ok(self.browse_artists.clone())
    }
    fn browse_albums(&self, _offset: i32, _limit: i32) -> HostResult<Page<AlbumData>> {
        self.record("browse_albums");
        Ok(self.browse_albums.clone())
    }
    fn browse_tracks(&self, _offset: i32, _limit: i32) -> HostResult<Page<Track>> {
        self.record("browse_tracks");
        Ok(self.browse_tracks.clone())
    }
    fn genre_artists(&self, genre: &str) -> HostResult<Vec<ArtistData>> {
        self.record(format!("genre_artists({genre})"));
        Ok(self.genre_artists.clone())
    }
    fn artist_albums(&self, artist: &str) -> HostResult<Vec<AlbumData>> {
        self.record(format!("artist_albums({artist})"));
        Ok(self.artist_albums.clone())
    }
    fn album_tracks(&self, album: &str) -> HostResult<Vec<Track>> {
        self.record(format!("album_tracks({album})"));
        Ok(self.album_tracks.clone())
    }
    fn genre_tracks(&self, genre: &str) -> HostResult<Vec<Track>> {
        self.record(format!("genre_tracks({genre})"));
        Ok(self.genre_tracks.clone())
    }
    fn album_cover(&self, artist: &str, album: &str, hash: &str) -> HostResult<AlbumCover> {
        self.record(format!("album_cover({artist},{album},{hash})"));
        Ok(self.album_cover.clone())
    }
    fn album_cover_page(&self, _offset: i32, _limit: i32) -> HostResult<Page<AlbumCoverItem>> {
        self.record("album_cover_page");
        Ok(self.album_cover_page.clone())
    }
    fn cover_cache_status(&self) -> HostResult<bool> {
        self.record("cover_cache_status");
        Ok(self.cover_cache_status)
    }
    fn album_identifiers(&self) -> HostResult<Vec<AlbumIdentifier>> {
        self.record("album_identifiers");
        Ok(self.album_identifiers.clone())
    }
    fn artwork_location(&self, path: &str) -> HostResult<ArtworkLocation> {
        self.record(format!("artwork_location({path})"));
        Ok(ArtworkLocation::default())
    }
    fn artwork_raw(&self, path: &str) -> HostResult<Vec<u8>> {
        self.record(format!("artwork_raw({path})"));
        Ok(self.artwork_raw.clone())
    }
    fn batch_metadata(&self, paths: Vec<String>) -> HostResult<Vec<TrackMetadata>> {
        self.record(format!("batch_metadata({})", paths.len()));
        Ok(self.batch_metadata.clone())
    }
    fn track_paths(&self) -> HostResult<Vec<String>> {
        self.record("track_paths");
        Ok(self.track_paths.clone())
    }
    fn excluded_paths(&self) -> HostResult<Vec<String>> {
        self.record("excluded_paths");
        Ok(self.excluded_paths.clone())
    }
    fn media_paths(&self, source: LibrarySource) -> HostResult<Vec<String>> {
        self.record(format!("media_paths({})", source as i32));
        Ok(self
            .media_paths
            .lock()
            .unwrap()
            .get(&(source as i32))
            .cloned()
            .unwrap_or_default())
    }
    fn tracks_for_paths(&self, paths: Vec<String>) -> HostResult<Vec<Track>> {
        self.record(format!("tracks_for_paths({})", paths.len()));
        Ok(self.tracks_for_paths.clone())
    }
    fn field_names(&self, fields: Vec<i32>) -> HostResult<Vec<String>> {
        self.record("field_names");
        Ok(fields
            .iter()
            .map(|f| self.field_names.get(f).cloned().unwrap_or_default())
            .collect())
    }
    fn tags_for_paths(&self, fields: Vec<i32>, paths: Vec<String>) -> HostResult<Vec<PathTags>> {
        self.record(format!("tags_for_paths({})", paths.len()));
        let files = self.file_tags.lock().unwrap();
        Ok(paths
            .into_iter()
            .map(|path| {
                let tags = files.get(&path);
                let values = fields
                    .iter()
                    .map(|f| tags.and_then(|t| t.get(f)).cloned().unwrap_or_default())
                    .collect();
                PathTags { path, values }
            })
            .collect())
    }
    fn now_playing_tags(&self, fields: Vec<i32>) -> HostResult<PathTags> {
        self.record("now_playing_tags");
        let path = self.track_info.path.clone();
        if path.is_empty() {
            return Ok(PathTags::default());
        }
        let mut read = self.tags_for_paths(fields, vec![path])?;
        Ok(read.pop().unwrap_or_default())
    }
    /// Writes into `file_tags` under the read id, so a read after the write
    /// sees it.
    fn write_now_playing_tag(
        &self,
        path: String,
        field: i32,
        read_field: i32,
        value: String,
    ) -> HostResult<TagWriteResult> {
        self.record(format!("write_now_playing_tag({field},{value})"));
        if path != self.track_info.path {
            return Ok(TagWriteResult {
                outcome: "stale_track".into(),
                ..TagWriteResult::default()
            });
        }
        let previous = self
            .file_tags
            .lock()
            .unwrap()
            .get(&path)
            .and_then(|tags| tags.get(&read_field).cloned())
            .unwrap_or_default();
        let kept = if self.dropped_fields.contains(&field) {
            previous.clone()
        } else {
            value
        };
        self.file_tags
            .lock()
            .unwrap()
            .entry(path)
            .or_default()
            .insert(read_field, kept.clone());
        Ok(TagWriteResult {
            outcome: "written".into(),
            value: kept,
            previous,
            reason: String::new(),
        })
    }
    /// Only the paths asked for, as the host answers: a caller that sums what
    /// comes back must get the window it asked about, not the whole fixture.
    fn tracks_detailed_for_paths(&self, paths: Vec<String>) -> HostResult<Vec<TrackTags>> {
        self.record(format!("tracks_detailed_for_paths({})", paths.len()));
        Ok(self
            .tracks_detailed
            .iter()
            .filter(|t| paths.contains(&t.src))
            .cloned()
            .collect())
    }
    fn sync_delta(&self, updated_since: i64) -> HostResult<SyncDelta> {
        self.record(format!("sync_delta({updated_since})"));
        Ok(self.sync_delta.clone())
    }
    fn radio_stations(&self, _offset: i32, _limit: i32) -> HostResult<Page<RadioStation>> {
        self.record("radio_stations");
        Ok(self.radio_stations.clone())
    }
    fn play_all(&self, shuffle: bool) -> HostResult<()> {
        self.record(format!("play_all({shuffle})"));
        Ok(())
    }
    fn playlists(&self, _offset: i32, _limit: i32) -> HostResult<Page<Playlist>> {
        self.record("playlists");
        Ok(self.playlists.clone())
    }
    fn play_playlist(&self, url: &str) -> HostResult<()> {
        self.record(format!("play_playlist({url})"));
        Ok(())
    }
    fn playlist_files(&self, url: &str) -> HostResult<PlaylistFiles> {
        self.record(format!("playlist_files({url})"));
        let mut files = self.playlist_files.clone();
        if let Some(written) = self.written_playlist.lock().unwrap().clone() {
            files.paths = written;
        }
        Ok(files)
    }
    fn playlist_catalog(&self, _offset: i32, _limit: i32) -> HostResult<Page<PlaylistEntry>> {
        self.record("playlist_catalog");
        Ok(self.playlist_catalog.clone())
    }
    fn create_playlist(&self, folder: &str, name: &str, paths: Vec<String>) -> HostResult<String> {
        self.record(format!(
            "create_playlist({folder}, {name}, {})",
            paths.len()
        ));
        if self.refuse_playlist_writes {
            return Err(HostError::Refused("refused".into()));
        }
        *self.written_playlist.lock().unwrap() = Some(paths);
        Ok(format!("{folder}/{name}.mbp"))
    }
    fn delete_playlist(&self, url: &str) -> HostResult<()> {
        self.record(format!("delete_playlist({url})"));
        Ok(())
    }
    fn append_to_playlist(&self, url: &str, paths: Vec<String>) -> HostResult<()> {
        self.record(format!("append_to_playlist({url}, {})", paths.len()));
        if self.refuse_playlist_writes {
            return Err(HostError::Refused("refused".into()));
        }
        let mut written = self.written_playlist.lock().unwrap();
        let mut now = written
            .clone()
            .unwrap_or_else(|| self.playlist_files.paths.clone());
        now.extend(paths);
        *written = Some(now);
        Ok(())
    }
    fn set_playlist_files(&self, url: &str, paths: Vec<String>) -> HostResult<()> {
        self.record(format!("set_playlist_files({url})"));
        if self.refuse_playlist_writes {
            return Err(HostError::Refused("refused".into()));
        }
        *self.written_playlist.lock().unwrap() = Some(paths);
        Ok(())
    }
    fn plugin_version(&self) -> HostResult<String> {
        self.record("plugin_version");
        Ok(self.plugin_version.clone())
    }
    fn set_background_task_message(&self, message: &str) -> HostResult<()> {
        self.record(format!("set_background_task_message({message})"));
        Ok(())
    }
}

#[cfg(test)]
mod bytes_tests {
    use super::Bytes;

    #[test]
    fn a_messagepack_bin_reads_as_its_bytes() {
        // bin8 holding three bytes, as MessagePack-CSharp writes a byte[].
        let bytes: Bytes = rmp_serde::from_slice(&[0xC4, 0x03, 0xFF, 0xD8, 0x01]).unwrap();
        assert_eq!(bytes.0, [0xFF, 0xD8, 0x01]);
        let empty: Bytes = rmp_serde::from_slice(&[0xC4, 0x00]).unwrap();
        assert!(empty.0.is_empty());
    }

    #[test]
    fn a_string_is_not_taken_for_bytes() {
        assert!(rmp_serde::from_slice::<Bytes>(&[0xA2, b'h', b'i']).is_err());
    }
}
