//! MessagePack DTOs crossing the FFI boundary.
//!
//! Field names are the MessagePack keys (contractless resolver on the C#
//! side). Every field name here must match the matching C# property name
//! in `core/Services/NativeBridgeDtos.cs` — the drift-guard tests in
//! `tests/schema_drift.rs` and `tests/Serialization/NativeDtoDriftTests.cs`
//! will fail if either side renames a field.

use serde::{Deserialize, Serialize};

/// Payload for single-boolean command variants:
/// `SetMute`, `SetShuffle`, `SetScrobble`, `SetAutoDj`.
#[derive(Debug, Serialize, Deserialize)]
pub struct SetBoolParams {
    pub value: bool,
}

/// Payload for `SetRepeat`. `mode` is a `RepeatMode` PascalCase string
/// (`"None" | "All" | "One" | "Undefined"`), matching the C# enum's
/// `[EnumMember(Value = "...")]` names used throughout the legacy protocol.
#[derive(Debug, Serialize, Deserialize)]
pub struct SetRepeatParams {
    pub mode: String,
}

/// Single-string payload for commands whose parameter is a free-form string:
/// `SetRating` (digit 0-5 or empty), `OutputSwitch` (device name),
/// `PlaylistPlay` (playlist URL).
#[derive(Debug, Serialize, Deserialize)]
pub struct StringValueParams {
    pub value: String,
}

/// Payload for `SetLfmRating`. `status` is a `LastfmStatus` PascalCase
/// string (`"Normal" | "Love" | "Ban"`).
#[derive(Debug, Serialize, Deserialize)]
pub struct SetLfmRatingParams {
    pub status: String,
}

/// Payload for commands that take a single integer index:
/// `NowPlayingListRemove`.
#[derive(Debug, Serialize, Deserialize)]
pub struct IndexParams {
    pub index: i32,
}

/// Payload for `NowPlayingListMove`.
#[derive(Debug, Serialize, Deserialize)]
pub struct MoveParams {
    pub from: i32,
    pub to: i32,
}

/// Payload for `LibraryQueue{Genre,Artist,Album,Track}` and the
/// `NowPlayingListSearch` command. `queue_type` is one of the legacy
/// protocol strings (`"now" | "next" | "last" | "add-all"`); `query` is
/// the user-supplied search term. The C# side resolves the meta-tag
/// from the `CommandType` arm.
#[derive(Debug, Serialize, Deserialize)]
pub struct LibraryQueueParams {
    pub queue_type: String,
    pub query: String,
}

/// Pagination payload for `NowPlayingList` and `RadioStations` queries.
/// `offset` is the starting index; `limit` caps the returned count.
#[derive(Debug, Serialize, Deserialize)]
pub struct PaginationParams {
    pub offset: i32,
    pub limit: i32,
}

/// Single-string-query payload for `LibrarySearch{Genre,Artist,Album,Title}`
/// and the hierarchical-navigation queries `LibraryGenreArtists`,
/// `LibraryArtistAlbums`, `LibraryAlbumTracks`.
#[derive(Debug, Serialize, Deserialize)]
pub struct QueryParams {
    pub query: String,
}

/// Paginated-browse payload. `album_artists` is only consulted by
/// `LibraryBrowseArtists`; the other browse variants ignore it.
#[derive(Debug, Serialize, Deserialize)]
pub struct BrowseParams {
    pub offset: i32,
    pub limit: i32,
    #[serde(default)]
    pub album_artists: bool,
}

/// Payload for `NowPlayingQueue`. `queue_type` is the legacy protocol
/// string (`"now" | "next" | "last" | "add-all"`); `files` is the list
/// of file URLs the client wants enqueued; `play` is the file to start
/// playback from when `queue_type == "add-all"` (empty otherwise).
#[derive(Debug, Serialize, Deserialize)]
pub struct NowPlayingQueueParams {
    pub queue_type: String,
    pub files: Vec<String>,
    pub play: String,
}

/// Payload for the `AlbumCover` query. `client_hash` is the hash the
/// client already has cached (empty = no cached copy); the C# side
/// returns a 304-shaped response when it matches.
#[derive(Debug, Serialize, Deserialize)]
pub struct AlbumCoverParams {
    pub artist: String,
    pub album: String,
    #[serde(default)]
    pub client_hash: String,
}
