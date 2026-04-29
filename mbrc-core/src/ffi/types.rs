use std::ffi::c_int;

/// Result codes for all FFI functions.
/// 0 = success, negative = error.
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
}

/// Notification types forwarded from MusicBee via Plugin.ReceiveNotification.
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
}

/// Query types for the fat `query_data` callback.
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
}

/// Command types for the fat `execute_command` callback.
///
/// Numeric values are part of the FFI contract and must match the
/// `Cmd*` constants in `plugin/Services/NativeBridge.cs`. Never
/// renumber or reuse a value.
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
}

/// Callback delegate types matching the C# side exactly.
///
/// Thin callbacks: direct function pointers for latency-sensitive player actions.
/// Fat callbacks: MessagePack-based dispatchers for queries and commands.
///
/// Layout must match C# `MbrcCallbacks` struct byte-for-byte (36 bytes on i686).
#[repr(C)]
pub struct MbrcCallbacks {
    // Thin callbacks (offsets 0-20 on i686)
    pub player_play_pause: Option<extern "C" fn() -> c_int>,
    pub player_stop: Option<extern "C" fn() -> c_int>,
    pub player_next: Option<extern "C" fn() -> c_int>,
    pub player_previous: Option<extern "C" fn() -> c_int>,
    pub player_set_volume: Option<extern "C" fn(c_int) -> c_int>,
    pub player_set_position: Option<extern "C" fn(c_int) -> c_int>,

    // Fat callbacks (offsets 24-28 on i686)
    pub query_data: Option<
        extern "C" fn(
            query_type: i32,
            params_buf: *const u8,
            params_len: u32,
            out_result_buf: *mut *mut u8,
            out_result_len: *mut u32,
        ) -> i32,
    >,
    pub execute_command: Option<
        extern "C" fn(
            command_type: i32,
            params_buf: *const u8,
            params_len: u32,
            out_result_buf: *mut *mut u8,
            out_result_len: *mut u32,
        ) -> i32,
    >,

    // Memory management
    pub free_buffer: Option<extern "C" fn(*mut u8)>,
}

// Function pointers are inherently Send + Sync (they're just addresses).
// The C# side guarantees thread-safe implementations.
unsafe impl Send for MbrcCallbacks {}
unsafe impl Sync for MbrcCallbacks {}

#[cfg(test)]
mod tests {
    use super::*;
    use std::mem::size_of;

    #[test]
    fn callbacks_struct_size() {
        // 9 pointers x 4 bytes (i686) = 36 bytes
        // On x86_64 this would be 72 bytes, but we only target i686
        assert_eq!(size_of::<MbrcCallbacks>(), 9 * size_of::<usize>());
    }

    #[test]
    fn result_codes_are_correct() {
        assert_eq!(MbrcResult::Ok as i32, 0);
        assert_eq!(MbrcResult::AlreadyInitialized as i32, -1);
        assert_eq!(MbrcResult::NotInitialized as i32, -2);
        assert_eq!(MbrcResult::NullPointer as i32, -8);
    }
}
