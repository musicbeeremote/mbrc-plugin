//! MessagePack parameter DTOs crossing the FFI boundary (Rust -> C#).
//!
//! Field names ARE the MessagePack keys (the C# side uses a contractless
//! resolver). Every field name here must match the matching C# property name
//! in `core/Services/NativeBridgeDtos.cs`; the `schema_drift` tests fail if
//! either side renames a field. Serialize with `rmp_serde::to_vec_named` so
//! these become name-keyed maps, not positional arrays.

use serde::{Deserialize, Serialize};

use crate::protocol::messages::{LastfmStatus, QueueType, RepeatMode};

/// Single-boolean command payload: `SetMute`, `SetShuffle`, `SetScrobble`.
#[derive(Debug, Serialize, Deserialize)]
pub struct SetBoolParams {
    pub value: bool,
}

/// `SetRepeat`. `mode` is the canonical `RepeatMode` (serialized as its variant
/// name, e.g. `"None"`); C# maps it to its own `RepeatMode`.
#[derive(Debug, Serialize, Deserialize)]
pub struct SetRepeatParams {
    pub mode: RepeatMode,
}

/// Single free-form string payload: `SetRating` (digit 0-5 or empty),
/// `OutputSwitch` (device name), `PlaylistPlay` (playlist URL).
#[derive(Debug, Serialize, Deserialize)]
pub struct StringValueParams {
    pub value: String,
}

/// `SetLfmRating`. `status` is the canonical `LastfmStatus` (variant name);
/// the `"toggle"` action is resolved to a concrete status by the handler.
#[derive(Debug, Serialize, Deserialize)]
pub struct SetLfmRatingParams {
    pub status: LastfmStatus,
}

/// Single integer index: `NowPlayingListRemove`, `NowPlayingListPlay`.
#[derive(Debug, Serialize, Deserialize)]
pub struct IndexParams {
    pub index: i32,
}

/// Single integer value: `SetVolume` (0-100), `SetPosition` (milliseconds).
#[derive(Debug, Serialize, Deserialize)]
pub struct SetIntParams {
    pub value: i32,
}

/// `NowPlayingListMove`.
#[derive(Debug, Serialize, Deserialize)]
pub struct MoveParams {
    pub from: i32,
    pub to: i32,
}

/// Pagination payload for `NowPlayingList` / `RadioStations` queries.
#[derive(Debug, Serialize, Deserialize)]
pub struct PaginationParams {
    pub offset: i32,
    pub limit: i32,
}

/// Single-string-query payload for hierarchical navigation queries
/// (`LibraryGenreArtists`, `LibraryArtistAlbums`, `LibraryAlbumTracks`).
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

/// `NowPlayingQueue`. `queue_type` is the canonical `QueueType` (the client's
/// `"now"`/`"next"`/`"last"`/`"add-all"` is parsed to it by the codec); `files`
/// are the URLs to enqueue; `play` is the file to start from for `AddAndPlay`
/// (empty otherwise).
#[derive(Debug, Serialize, Deserialize)]
pub struct NowPlayingQueueParams {
    pub queue_type: QueueType,
    pub files: Vec<String>,
    pub play: String,
}

/// `NowPlayingTagChange`. `tag` is the lowercase wire tag name; `value` is the
/// new tag value. C# maps `tag` to its `MetaDataType` and commits it.
#[derive(Debug, Serialize, Deserialize)]
pub struct TagChangeParams {
    pub tag: String,
    pub value: String,
}

/// `AlbumCover` query. `client_hash` is the hash the client already cached
/// (empty = none); C# returns a 304-shaped response when it matches.
#[derive(Debug, Serialize, Deserialize)]
pub struct AlbumCoverParams {
    pub artist: String,
    pub album: String,
    #[serde(default)]
    pub client_hash: String,
}

/// `ArtworkRawForPath` query: the representative track path whose raw MusicBee
/// artwork the host returns (base64). The core decodes, resizes, hashes, and
/// stores it during a cover-cache build.
#[derive(Debug, Serialize, Deserialize)]
pub struct PathParams {
    pub path: String,
}

/// `BatchMetadata` query: the track paths to resolve to `{artist, album}` in one
/// host call (the paginated cover grid needs a display artist/album per cover).
#[derive(Debug, Serialize, Deserialize)]
pub struct BatchMetadataParams {
    pub paths: Vec<String>,
}

/// `LibraryTracksForPaths` query (MBRCIP-0001): the track paths - a single browse
/// page's slice of the ordinal index - whose 7 browse tags the host reads in one
/// batch, so the core fills its path-keyed tag cache for just that page and never
/// materializes the whole library. One `Library_GetFileTags` per path on the C#
/// side. (Distinct from `BatchMetadataParams` despite the identical shape: that
/// resolves 2 cover fields, this resolves the full 7 browse fields.)
#[derive(Debug, Serialize, Deserialize)]
pub struct PathsParams {
    pub paths: Vec<String>,
}

/// `LibrarySyncDelta` query (MBRCIP-0001): the watermark (unix seconds) the host
/// lists library changes after. C# maps it to a `DateTime` for
/// `Library_GetSyncDelta`. The core stores this as `tracks:synced_at` and passes
/// it back each scan to pull only what changed.
#[derive(Debug, Serialize, Deserialize)]
pub struct SyncDeltaParams {
    pub updated_since: i64,
}
