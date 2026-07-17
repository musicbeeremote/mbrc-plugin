//! The C-ABI surface between the Rust core and the C# plugin shim.
//!
//! The `QueryType` / `CommandType` / `NotificationType` numeric values and the
//! `MbrcCallbacks` layout are the contract; the matching `Query*` / `Cmd*`
//! constants and the `MbrcCallbacks` struct on the C# side
//! (`plugin/Services/NativeBridge.cs`) must stay identical. The contract is
//! being *finalized* pre-cutover (no live C# consumer yet). After cutover it is
//! additive-only: append enum variants, never renumber; and bump
//! [`MBRC_ABI_VERSION`] on any incompatible change so a stale DLL is rejected at
//! `mbrc_initialize` instead of decoding garbage.

/// The ABI contract version C# is compiled against and passes to
/// `mbrc_initialize`. Bump this on ANY incompatible change to the exports,
/// `MbrcCallbacks`, or the enum numbering, so a mismatched `mbrc_core.dll`
/// next to the shim (or a dev build skew) is rejected up front.
pub const MBRC_ABI_VERSION: i32 = 1;

/// Result codes for all FFI functions. `0` = success, negative = error.
#[repr(i32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[allow(dead_code)]
pub enum MbrcResult {
    Ok = 0,
    AlreadyInitialized = -1,
    NotInitialized = -2,
    AlreadyRunning = -3,
    NotRunning = -4,
    RuntimeError = -5,
    InvalidArgument = -6,
    CallbackError = -7,
    NullPointer = -8,
    /// C# passed an `abi_version` that does not match [`MBRC_ABI_VERSION`].
    AbiVersionMismatch = -9,
}

/// Notifications forwarded from MusicBee via `Plugin.ReceiveNotification`.
#[repr(i32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum NotificationType {
    TrackChanged = 0,
    PlayStateChanged = 1,
    VolumeLevelChanged = 2,
    VolumeMuteChanged = 3,
    NowPlayingLyricsReady = 4,
    NowPlayingArtworkReady = 5,
    NowPlayingListChanged = 6,
    FileAddedToLibrary = 7,
    /// The active MusicBee library changed. The metadata + cover caches are
    /// keyed to a single library, so this invalidates and rebuilds them.
    LibrarySwitched = 8,
}

impl NotificationType {
    /// Map the raw i32 from the FFI boundary to a known notification.
    pub fn from_i32(value: i32) -> Option<Self> {
        match value {
            0 => Some(Self::TrackChanged),
            1 => Some(Self::PlayStateChanged),
            2 => Some(Self::VolumeLevelChanged),
            3 => Some(Self::VolumeMuteChanged),
            4 => Some(Self::NowPlayingLyricsReady),
            5 => Some(Self::NowPlayingArtworkReady),
            6 => Some(Self::NowPlayingListChanged),
            7 => Some(Self::FileAddedToLibrary),
            8 => Some(Self::LibrarySwitched),
            _ => None,
        }
    }
}

/// Query types for the fat `query_data` callback (C# reads MusicBee data).
///
/// Numeric values are part of the FFI contract and must match the `Query*`
/// constants in `plugin/Services/NativeBridge.cs`. Never renumber or reuse a
/// value. Some variants (library search/queue) are reserved for the C# side
/// and are not issued by the maintained V4 wire surface.
#[repr(i32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[allow(dead_code)]
pub enum QueryType {
    PlayerState = 1,
    TrackInfo = 2,
    CoverData = 3,
    Lyrics = 4,
    PlaylistList = 5,
    NowPlayingList = 6,
    RadioStations = 7,
    OutputDevices = 8,
    LibrarySearchGenre = 9,
    LibrarySearchArtist = 10,
    LibrarySearchAlbum = 11,
    LibrarySearchTitle = 12,
    LibraryBrowseGenres = 13,
    LibraryBrowseArtists = 14,
    LibraryBrowseAlbums = 15,
    LibraryBrowseTracks = 16,
    LibraryGenreArtists = 17,
    LibraryArtistAlbums = 18,
    LibraryAlbumTracks = 19,
    NowPlayingDetails = 20,
    AlbumCover = 21,
    CoverCacheBuildStatus = 22,
    PlaybackPosition = 23,
    AlbumCoverBatch = 24,
    NowPlayingRating = 25,
    NowPlayingLfmRating = 26,
    NowPlayingListOrdered = 27,
    // The plugin version string (C# `IUserSettings.CurrentVersion`).
    PluginVersion = 28,
    // Cover-cache leaf providers: the host supplies raw ingredients, the core
    // owns resize/hash/cache/serve. These replace the old resized-cover queries
    // (`AlbumCover = 21`, `CoverCacheBuildStatus = 22`, `AlbumCoverBatch = 24`),
    // which stay RESERVED above (the core stops issuing them once the rewire in
    // stage 3 lands). `AlbumIdentifiers` is ONE library scan folded into
    // per-album identities (no more 2-3 passes); `ArtworkRawForPath` returns a
    // track's raw MusicBee artwork (base64); `BatchMetadata` resolves paths to
    // {artist, album} for the paginated cover grid.
    AlbumIdentifiers = 29,
    ArtworkRawForPath = 30,
    BatchMetadata = 31,
    // Library cache (MBRCIP-0001): the core owns an on-disk ordinal path index +
    // path-keyed tag cache, so browse pages are served O(page) without ever
    // materializing the whole library. `LibraryTrackPaths` returns every track
    // path in browse order in one `Library_QueryFilesEx(null)` call (the ordinal
    // index, source of truth). `LibraryTracksForPaths` batch-reads the 7 browse
    // tags for just a page's paths (one `Library_GetFileTags` per path), filling
    // the path-keyed tag cache lazily. `LibrarySyncDelta` lists paths changed
    // since a watermark, for incremental refresh by the background Scanner.
    LibraryTrackPaths = 32,
    LibraryTracksForPaths = 33,
    LibrarySyncDelta = 34,
}

/// Command types for the fat `execute_command` callback (C# mutates state).
///
/// Numeric values are part of the FFI contract and must match the `Cmd*`
/// constants in `plugin/Services/NativeBridge.cs`. Never renumber or reuse a
/// value.
#[repr(i32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[allow(dead_code)]
pub enum CommandType {
    _Reserved = 0,
    SetMute = 1,
    SetShuffle = 2,
    SetRepeat = 3,
    SetScrobble = 4,
    SetAutoDj = 5,
    SetRating = 6,
    SetLfmRating = 7,
    OutputSwitch = 8,
    PlaylistPlay = 9,
    LibraryPlayAll = 10,
    NowPlayingListPlay = 11,
    NowPlayingListMove = 12,
    NowPlayingListRemove = 13,
    NowPlayingListSearch = 14,
    LibraryQueueGenre = 15,
    LibraryQueueArtist = 16,
    LibraryQueueAlbum = 17,
    LibraryQueueTrack = 18,
    NowPlayingQueue = 19,
    NowPlayingTagChange = 20,
    // Player transport. Collapsed from the former thin callbacks so every
    // action flows through the single `execute_command` path.
    PlayPause = 21,
    Stop = 22,
    Next = 23,
    Previous = 24,
    SetVolume = 25,
    SetPosition = 26,
    // Explicit play/pause (distinct from the PlayPause toggle) - the Android
    // media session sends these separately.
    Play = 27,
    Pause = 28,
    // Set MusicBee's status-bar background-task text (host-only UI). The core
    // uses it to surface cover-cache build progress now that it owns the build.
    SetBackgroundTaskMessage = 29,
}

/// Host -> core queries (request/response), the mirror of [`QueryType`] in the
/// opposite direction: the C# host asks the core for data it owns and gets a
/// MessagePack buffer back (via `mbrc_query`). Use this for app-level reads the
/// UI needs - cache health, denied-client history, diagnostics - instead of a
/// bespoke export per call. Numeric values are contract: additive, never
/// renumbered; bump [`MBRC_ABI_VERSION`] on an incompatible change.
#[repr(i32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[allow(dead_code)]
pub enum HostQueryType {
    /// Cache health for the settings panel (covers cached, building, ready).
    CacheStatus = 1,
    /// Recently rejected connection attempts (address filter / caps) for the
    /// settings panel's "Blocked connections" view. Returns a MessagePack array
    /// of `BlockedConnection`, newest first.
    RecentBlocked = 2,
}

impl HostQueryType {
    pub fn from_i32(value: i32) -> Option<Self> {
        match value {
            1 => Some(Self::CacheStatus),
            2 => Some(Self::RecentBlocked),
            _ => None,
        }
    }
}

/// Host -> core commands (fire-and-forget), the mirror of [`CommandType`]: the
/// C# host asks the core to perform an action, getting only a status back (via
/// `mbrc_command`). Same numbering discipline as [`HostQueryType`].
#[repr(i32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[allow(dead_code)]
pub enum HostCommandType {
    /// Rebuild only the metadata cache (browse lists: genres/artists/albums/
    /// tracks) in the background. Cheap - a few library scans, no cover work.
    RebuildMetadata = 1,
    /// Rebuild only the cover cache in the background. Expensive - re-fetches and
    /// re-resizes artwork per album.
    RebuildCovers = 2,
    /// Clear the in-memory blocked-connection log (the panel's "Clear" button).
    ClearBlockedLog = 3,
}

impl HostCommandType {
    pub fn from_i32(value: i32) -> Option<Self> {
        match value {
            1 => Some(Self::RebuildMetadata),
            2 => Some(Self::RebuildCovers),
            3 => Some(Self::ClearBlockedLog),
            _ => None,
        }
    }
}

/// Core -> host push events, delivered through the `on_event` callback. The core
/// invokes it (from a background thread) when host-relevant state changes so an
/// open UI can refresh without polling. The payload is an optional MessagePack
/// buffer (empty when the host should just re-query). Same numbering discipline
/// as the other enums. Distinct from [`NotificationType`], which flows the other
/// way (MusicBee -> core).
#[repr(i32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[allow(dead_code)]
pub enum HostEventType {
    /// The cache status changed (a reconcile/rebuild started or finished); the
    /// settings panel should re-query [`HostQueryType::CacheStatus`].
    CacheStatusChanged = 1,
}

/// Callback table handed from C# to Rust at `mbrc_initialize`.
///
/// Two callbacks carry the in-process RPC, with distinct shapes matching their
/// roles:
/// - **`query_data` (get, request/response)**: reads MusicBee data (see
///   [`QueryType`]) and returns a MessagePack result buffer via the out-params.
/// - **`execute_command` (update, one-way)**: mutates state fire-and-forget (see
///   [`CommandType`]); returns *only* a status - no result buffer. Handlers that
///   need to report new state re-query afterward.
///
/// `free_buffer` releases the C#-allocated result buffer from a `query_data`
/// call. Transport controls are ordinary `CommandType` variants (no special
/// player path).
///
/// **Callback status contract (C# -> Rust):** `0` = success. For `query_data`,
/// success MUST return a valid, non-empty MessagePack buffer; a domain "not
/// found" is encoded *inside* the payload (e.g. `Cover{status:404}`), never as
/// an empty buffer. A non-zero status means the C# provider threw - the core
/// logs it and sends no reply. `query_data` returning `status 0` with a null or
/// empty buffer is a contract violation and is treated as an error.
///
/// Layout is 4 pointers (16 bytes on i686); the C# `MbrcCallbacks` struct must
/// match. Being finalized pre-cutover; afterward, extend through the enums.
#[repr(C)]
pub struct MbrcCallbacks {
    /// Read MusicBee data (request/response). See [`QueryType`].
    pub query_data: Option<
        extern "C" fn(
            query_type: i32,
            params_buf: *const u8,
            params_len: u32,
            out_result_buf: *mut *mut u8,
            out_result_len: *mut u32,
        ) -> i32,
    >,
    /// Mutate MusicBee state, fire-and-forget (one-way). See [`CommandType`].
    /// Returns only a status; there is no result buffer.
    pub execute_command:
        Option<extern "C" fn(command_type: i32, params_buf: *const u8, params_len: u32) -> i32>,
    /// Release a buffer previously returned through `query_data`'s out-pointer.
    pub free_buffer: Option<extern "C" fn(*mut u8)>,
    /// Core -> host push notification (one-way). The core calls this from a
    /// background thread when host-relevant state changes; the host raises it to
    /// any open UI. See [`HostEventType`]. Payload is an optional MessagePack
    /// buffer (empty = "re-query"). Null when the host wants no events.
    pub on_event: Option<extern "C" fn(event_type: i32, payload_buf: *const u8, payload_len: u32)>,
}

// Function pointers are inherently Send + Sync (they are just addresses); the
// C# side guarantees thread-safe implementations.
unsafe impl Send for MbrcCallbacks {}
unsafe impl Sync for MbrcCallbacks {}

#[cfg(test)]
mod tests {
    use super::*;
    use std::mem::size_of;

    #[test]
    fn callbacks_struct_size_is_four_pointers() {
        // 4 pointers x 4 bytes on i686 = 16 bytes. The C# struct must match.
        assert_eq!(size_of::<MbrcCallbacks>(), 4 * size_of::<usize>());
    }

    #[test]
    fn result_codes_match_the_contract() {
        assert_eq!(MbrcResult::Ok as i32, 0);
        assert_eq!(MbrcResult::AlreadyInitialized as i32, -1);
        assert_eq!(MbrcResult::NotInitialized as i32, -2);
        assert_eq!(MbrcResult::NullPointer as i32, -8);
    }

    #[test]
    fn notification_type_roundtrips() {
        for raw in 0..=8 {
            assert_eq!(NotificationType::from_i32(raw).map(|n| n as i32), Some(raw));
        }
        assert_eq!(NotificationType::from_i32(9), None);
        assert_eq!(NotificationType::from_i32(-1), None);
    }

    #[test]
    fn host_enums_roundtrip_and_reject_unknown() {
        assert_eq!(HostQueryType::from_i32(1), Some(HostQueryType::CacheStatus));
        assert_eq!(
            HostQueryType::from_i32(2),
            Some(HostQueryType::RecentBlocked)
        );
        assert_eq!(HostQueryType::from_i32(0), None);
        assert_eq!(HostQueryType::from_i32(3), None);
        assert_eq!(
            HostCommandType::from_i32(1),
            Some(HostCommandType::RebuildMetadata)
        );
        assert_eq!(
            HostCommandType::from_i32(2),
            Some(HostCommandType::RebuildCovers)
        );
        assert_eq!(
            HostCommandType::from_i32(3),
            Some(HostCommandType::ClearBlockedLog)
        );
        assert_eq!(HostCommandType::from_i32(4), None);
        // HostEventType is core -> host (no from_i32), but its contract value is
        // still pinned so the C# enum stays in sync.
        assert_eq!(HostEventType::CacheStatusChanged as i32, 1);
    }
}
