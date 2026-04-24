namespace MusicBeePlugin.Services
{
    /// <summary>
    ///     Data-transfer objects exchanged between the C# host and the Rust
    ///     native core via MessagePack. Lives in <c>core</c> (not <c>plugin</c>)
    ///     so the test project can round-trip the canonical JSON schemas under
    ///     <c>mbrc-core/tests/schemas</c> without pulling in the full plugin
    ///     assembly. Property names are the MessagePack keys under
    ///     <see cref="MessagePack.Resolvers.ContractlessStandardResolver"/>;
    ///     they must match the Rust serde field names exactly.
    /// </summary>
    public class PlayerStateDto
    {
        public string play_state { get; set; }
        public int volume { get; set; }
        public bool mute { get; set; }
        public string shuffle { get; set; }
        public string repeat { get; set; }
        public int position { get; set; }
        public bool scrobble { get; set; }
    }

    public class TrackInfoDto
    {
        public string artist { get; set; }
        public string title { get; set; }
        public string album { get; set; }
        public string year { get; set; }
        public string path { get; set; }
    }

    /// <summary>
    ///     Payload for single-boolean command variants (SetMute, SetShuffle,
    ///     SetScrobble, SetAutoDj).
    /// </summary>
    public class SetBoolParams
    {
        public bool value { get; set; }
    }

    /// <summary>
    ///     Payload for SetRepeat. <c>mode</c> is the PascalCase
    ///     <see cref="Enumerations.RepeatMode"/> name ("None", "All", "One",
    ///     "Undefined") — the legacy protocol's on-wire form.
    /// </summary>
    public class SetRepeatParams
    {
        public string mode { get; set; }
    }

    /// <summary>
    ///     Single-string payload (SetRating, OutputSwitch, PlaylistPlay).
    /// </summary>
    public class StringValueParams
    {
        public string value { get; set; }
    }

    /// <summary>
    ///     Payload for SetLfmRating. <c>status</c> is a
    ///     <see cref="Enumerations.LastfmStatus"/> name ("Normal"/"Love"/"Ban").
    /// </summary>
    public class SetLfmRatingParams
    {
        public string status { get; set; }
    }

    /// <summary>
    ///     Single integer index payload (NowPlayingListRemove).
    /// </summary>
    public class IndexParams
    {
        public int index { get; set; }
    }

    /// <summary>
    ///     Payload for NowPlayingListMove.
    /// </summary>
    public class MoveParams
    {
        public int from { get; set; }
        public int to { get; set; }
    }

    /// <summary>
    ///     Payload for LibraryQueue{Genre,Artist,Album,Track}.
    ///     <c>queue_type</c> is a legacy protocol string
    ///     (<c>"now" | "next" | "last" | "add-all"</c>).
    /// </summary>
    public class LibraryQueueParams
    {
        public string queue_type { get; set; }
        public string query { get; set; }
    }

    /// <summary>
    ///     Pagination payload (NowPlayingList, RadioStations queries).
    /// </summary>
    public class PaginationParams
    {
        public int offset { get; set; }
        public int limit { get; set; }
    }

    public class PlaylistDto
    {
        public string url { get; set; }
        public string name { get; set; }
    }

    public class PlaylistListResponse
    {
        public System.Collections.Generic.List<PlaylistDto> playlists { get; set; }
    }

    /// <summary>
    ///     One track in a paginated NowPlayingList response.
    /// </summary>
    public class NowPlayingTrackDto
    {
        public string artist { get; set; }
        public string album { get; set; }
        public string album_artist { get; set; }
        public string title { get; set; }
        public string path { get; set; }
        public int position { get; set; }
    }

    public class NowPlayingListResponse
    {
        public System.Collections.Generic.List<NowPlayingTrackDto> tracks { get; set; }
    }

    public class RadioStationDto
    {
        public string name { get; set; }
        public string url { get; set; }
    }

    public class RadioStationsResponse
    {
        public System.Collections.Generic.List<RadioStationDto> stations { get; set; }
    }

    /// <summary>
    ///     Response DTO for OutputDevices. Matches the Rust serde struct;
    ///     note the on-wire field names differ from the legacy
    ///     <see cref="MusicBeePlugin.Models.Entities.OutputDevice"/> shape
    ///     (<c>devices</c> is an array of plain strings rather than a nested
    ///     object, since the Rust core re-serializes for its own protocol).
    /// </summary>
    public class OutputDevicesResponse
    {
        public string active { get; set; }
        public string[] devices { get; set; }
    }

    /// <summary>Single-query payload for LibrarySearch/LibraryGenreArtists/ArtistAlbums/AlbumTracks.</summary>
    public class QueryParams
    {
        public string query { get; set; }
    }

    /// <summary>Browse payload. <c>album_artists</c> only read by LibraryBrowseArtists.</summary>
    public class BrowseParams
    {
        public int offset { get; set; }
        public int limit { get; set; }
        public bool album_artists { get; set; }
    }

    public class AlbumCoverParams
    {
        public string artist { get; set; }
        public string album { get; set; }
        public string client_hash { get; set; }
    }

    public class GenreDto
    {
        public string genre { get; set; }
        public int count { get; set; }
    }

    public class GenreListResponse
    {
        public System.Collections.Generic.List<GenreDto> genres { get; set; }
    }

    public class ArtistDto
    {
        public string artist { get; set; }
        public int count { get; set; }
    }

    public class ArtistListResponse
    {
        public System.Collections.Generic.List<ArtistDto> artists { get; set; }
    }

    public class AlbumDto
    {
        public string artist { get; set; }
        public string album { get; set; }
        public int count { get; set; }
    }

    public class AlbumListResponse
    {
        public System.Collections.Generic.List<AlbumDto> albums { get; set; }
    }

    public class TrackDto
    {
        public string src { get; set; }
        public string artist { get; set; }
        public string title { get; set; }
        public int trackno { get; set; }
        public int disc { get; set; }
        public string album { get; set; }
        public string album_artist { get; set; }
        public string genre { get; set; }
    }

    public class TrackListResponse
    {
        public System.Collections.Generic.List<TrackDto> tracks { get; set; }
    }

    /// <summary>
    ///     NowPlayingDetails uses legacy camelCase names (serde rename on
    ///     the Rust side). Everything is a string — MusicBee exposes raw
    ///     tag representations.
    /// </summary>
    public class NowPlayingDetailsResponse
    {
        public string albumArtist { get; set; }
        public string genre { get; set; }
        public string trackNo { get; set; }
        public string trackCount { get; set; }
        public string discNo { get; set; }
        public string discCount { get; set; }
        public string publisher { get; set; }
        public string composer { get; set; }
        public string comment { get; set; }
        public string grouping { get; set; }
        public string ratingAlbum { get; set; }
        public string encoder { get; set; }
        public string kind { get; set; }
        public string format { get; set; }
        public string size { get; set; }
        public string channels { get; set; }
        public string sampleRate { get; set; }
        public string bitrate { get; set; }
        public string dateModified { get; set; }
        public string dateAdded { get; set; }
        public string lastPlayed { get; set; }
        public string playCount { get; set; }
        public string skipCount { get; set; }
        public string duration { get; set; }
    }

    public class AlbumCoverResponse
    {
        public string album { get; set; }
        public string artist { get; set; }
        public string cover { get; set; }
        public int status { get; set; }
        public string hash { get; set; }
    }

    public class CoverCacheBuildStatusResponse
    {
        public bool building { get; set; }
    }

    public class PlaybackPositionResponse
    {
        public int current { get; set; }
        public int total { get; set; }
    }
}
