//! FFI query response DTOs: the shapes the C# side returns over `query_data`
//! (Rust deserializes these). Field names are the MessagePack keys and match
//! the C# `NativeBridgeDtos`; the wire shapes sent to clients are built from
//! these by the `legacy_v4` formatter (which applies the V4 quirks). Slices 2/3
//! grow this with the full response surface.

use serde::{Deserialize, Serialize};

/// Reply for `PlaybackPosition` (and the V5 `nowplayingcurrentposition`
/// affordance): current and total playback position in milliseconds.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct PlaybackPositionResponse {
    pub current: i32,
    pub total: i32,
}

/// Canonical playback state. Serde uses the variant names as the FFI/RPC tokens
/// (C# sends `"Playing"` etc.); the V4 *wire* spelling is a separate concern
/// owned by the `wire::v4` formatter (a future V6 formatter renders differently).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
pub enum PlayState {
    #[default]
    Undefined,
    Stopped,
    Playing,
    Paused,
}

/// Canonical shuffle mode. FFI token = variant name (`"Off"`); V4 wire = `"off"`
/// (mapped by the formatter).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
pub enum ShuffleMode {
    #[default]
    Off,
    Shuffle,
    AutoDj,
}

/// Canonical repeat mode. FFI token = variant name; V4 wire coincides with it.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
pub enum RepeatMode {
    #[default]
    Undefined,
    None,
    All,
    One,
}

/// Canonical Last.fm love/ban status. FFI token = variant name; the V4 wire
/// spelling coincides with it (owned by the formatter).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
pub enum LastfmStatus {
    #[default]
    Normal,
    Love,
    Ban,
}

/// Canonical now-playing-queue placement. FFI token = variant name (matching the
/// C# `QueueType`); the V4 client wire input (`"now"`/`"next"`/`"last"`/
/// `"add-all"`) is parsed to this by the wire codec.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
pub enum QueueType {
    Last,
    #[default]
    Next,
    PlayNow,
    AddAndPlay,
}

/// Full player state (FFI query `PlayerState`), strictly typed. The wire
/// `playerstatus` object (with `volume` stringified) is derived from this by the
/// `wire::v4` formatter.
#[derive(Debug, Clone, Default, Deserialize)]
pub struct PlayerState {
    #[serde(default)]
    pub play_state: PlayState,
    #[serde(default)]
    pub volume: i32,
    #[serde(default)]
    pub mute: bool,
    #[serde(default)]
    pub shuffle: ShuffleMode,
    #[serde(default)]
    pub repeat: RepeatMode,
    #[serde(default)]
    pub position: i32,
    #[serde(default)]
    pub scrobble: bool,
}

/// Audio output devices (FFI query `OutputDevices`). Sent to the wire as-is on
/// the `playeroutput` context.
#[derive(Debug, Clone, Default, Deserialize)]
pub struct OutputDevices {
    #[serde(default)]
    pub active: String,
    #[serde(default)]
    pub devices: Vec<String>,
}

/// Current track (FFI query `TrackInfo`). Field names already match the
/// `nowplayingtrack` wire shape, so it serializes straight to the client.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct TrackInfo {
    #[serde(default)]
    pub artist: String,
    #[serde(default)]
    pub title: String,
    #[serde(default)]
    pub album: String,
    #[serde(default)]
    pub year: String,
    #[serde(default)]
    pub path: String,
}

/// Extended track metadata (FFI query `NowPlayingDetails`). All values are
/// strings; camelCase keys match the `nowplayingdetails` wire shape directly.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase", default)]
pub struct TrackDetails {
    pub album_artist: String,
    pub genre: String,
    pub track_no: String,
    pub track_count: String,
    pub disc_no: String,
    pub disc_count: String,
    pub publisher: String,
    pub composer: String,
    pub comment: String,
    pub grouping: String,
    pub rating_album: String,
    pub encoder: String,
    pub kind: String,
    pub format: String,
    pub size: String,
    pub channels: String,
    pub sample_rate: String,
    pub bitrate: String,
    pub date_modified: String,
    pub date_added: String,
    pub last_played: String,
    pub play_count: String,
    pub skip_count: String,
    pub duration: String,
}

/// Cover payload (FFI query `CoverData`). `cover` is omitted on the wire when
/// empty (status 1 = building, 404 = not found).
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct Cover {
    #[serde(default)]
    pub status: i32,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub cover: String,
}

/// Lyrics payload (FFI query `Lyrics`). `lyrics` is always present (empty when
/// status is 404).
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct Lyrics {
    #[serde(default)]
    pub status: i32,
    #[serde(default)]
    pub lyrics: String,
}

/// Paginated envelope shared by every list endpoint (now playing, browse,
/// playlists, radio). Field order matches the shipped C# plugin exactly:
/// `{total,offset,limit,data}` (with preserve_order, declaration order is the
/// wire order).
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Page<T> {
    pub total: i32,
    pub offset: i32,
    pub limit: i32,
    #[serde(default)]
    pub data: Vec<T>,
}

// Manual Default so it doesn't require `T: Default`.
impl<T> Default for Page<T> {
    fn default() -> Self {
        Self {
            offset: 0,
            limit: 0,
            total: 0,
            data: Vec::new(),
        }
    }
}

/// A now-playing-list item. Canonical shape carries every field; the V4 wire
/// codec decides per platform whether `album`/`album_artist` are emitted (iOS
/// yes even when empty, Android no) - see `wire::v4::now_playing_list`.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct NowPlayingListTrack {
    #[serde(default)]
    pub artist: String,
    #[serde(default)]
    pub album: String,
    #[serde(default)]
    pub album_artist: String,
    #[serde(default)]
    pub title: String,
    #[serde(default)]
    pub path: String,
    #[serde(default)]
    pub position: i32,
}

/// Library browse/search items. `count` is the number of tracks under the entry.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct GenreData {
    #[serde(default)]
    pub genre: String,
    #[serde(default)]
    pub count: i32,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct ArtistData {
    #[serde(default)]
    pub artist: String,
    #[serde(default)]
    pub count: i32,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct AlbumData {
    #[serde(default)]
    pub album: String,
    #[serde(default)]
    pub artist: String,
    #[serde(default)]
    pub count: i32,
}

/// A library track. `album`/`genre` are omitted when empty - the iOS
/// `libraryalbumtracks` items carry neither, while flat `browsetracks` do.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct Track {
    #[serde(default)]
    pub src: String,
    #[serde(default)]
    pub artist: String,
    #[serde(default)]
    pub title: String,
    #[serde(default)]
    pub trackno: i32,
    #[serde(default)]
    pub disc: i32,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub album: String,
    #[serde(default)]
    pub album_artist: String,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub genre: String,
}

/// Single-cover response (`libraryalbumcover`). Field order matches the shipped
/// C# `AlbumCoverPayload` (album, artist, cover, status, hash); everything but
/// `status` is omitted when empty, so a typical single-cover reply is
/// `{cover, status, hash}` and a miss is `{status:404}`.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct AlbumCover {
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub album: String,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub artist: String,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub cover: String,
    #[serde(default)]
    pub status: i32,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub hash: String,
}

/// One album's cache identity from the host's single-pass library scan
/// (`AlbumIdentifiers`): the representative track `path` (artwork source), its
/// `artist`/`album` tags, and the file's modification time as unix seconds. The
/// core derives the cache key via `cover_identifier(artist, album)` - the host
/// does no hashing, keeping identity in one place.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct AlbumIdentifier {
    #[serde(default)]
    pub artist: String,
    #[serde(default)]
    pub album: String,
    #[serde(default)]
    pub path: String,
    #[serde(default)]
    pub modified: i64,
}

/// One track's display `{artist, album}` for the paginated cover grid
/// (`BatchMetadata`), keyed back by its `path`.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct TrackMetadata {
    #[serde(default)]
    pub path: String,
    #[serde(default)]
    pub artist: String,
    #[serde(default)]
    pub album: String,
}

/// Library changes since a watermark (`LibrarySyncDelta`, MBRCIP-0001). Each
/// list holds track paths. The background Scanner drops `added`/`updated` paths
/// from the path-keyed tag cache (they are re-read lazily on next serve) and can
/// use `deleted` directly; adds/reorders are also caught by re-fetching the
/// ordinal path index, so `deleted` is a convenience, not the sole delete source.
/// Not wire-visible - an internal FFI DTO, so field names are chosen for clarity
/// (Rust cannot name a field `new`), not protocol compat.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct SyncDelta {
    #[serde(default)]
    pub added: Vec<String>,
    #[serde(default)]
    pub updated: Vec<String>,
    #[serde(default)]
    pub deleted: Vec<String>,
}

/// One item of a paginated album-cover page.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct AlbumCoverItem {
    #[serde(default)]
    pub album: String,
    #[serde(default)]
    pub artist: String,
    #[serde(default)]
    pub cover: String,
    #[serde(default)]
    pub status: i32,
    #[serde(default)]
    pub hash: String,
}

/// A playlist (`playlistlist` item).
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct Playlist {
    #[serde(default)]
    pub url: String,
    #[serde(default)]
    pub name: String,
}

/// A radio station (`radiostations` item).
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct RadioStation {
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub url: String,
}
