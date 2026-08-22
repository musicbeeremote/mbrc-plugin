using System.Collections.Generic;
using MusicBeePlugin.Models;
using MusicBeePlugin.Ffi;

namespace MusicBeePlugin.Providers
{
    /// <summary>
    ///     Data provider interface for library operations.
    ///     Returns clean domain objects with no MusicBee-specific knowledge.
    ///     All MusicBee API interaction (query lifecycle, null separators, etc.) stays in the implementation.
    ///     Methods returning IEnumerable use yield return for lazy evaluation and proper cleanup.
    /// </summary>
    public interface ILibraryDataProvider
    {
        // Radio Stations

        /// <summary>
        ///     Gets radio stations from the library.
        /// </summary>
        /// <param name="offset">Number of stations to skip</param>
        /// <param name="limit">Maximum number of stations to return</param>
        List<RadioStation> GetRadioStations(int offset = 0, int limit = 4000);

        // Browse Operations

        /// <summary>
        ///     Browses all genres in the library.
        ///     Uses yield return for streaming results and proper query cleanup.
        /// </summary>
        /// <param name="offset">Number of genres to skip</param>
        /// <param name="limit">Maximum number of genres to return</param>
        IEnumerable<GenreData> BrowseGenres(int offset = 0, int limit = 4000);

        /// <summary>
        ///     Browses all artists in the library.
        ///     Uses yield return for streaming results and proper query cleanup.
        /// </summary>
        /// <param name="offset">Number of artists to skip</param>
        /// <param name="limit">Maximum number of artists to return</param>
        /// <param name="albumArtists">If true, returns album artists; otherwise returns track artists</param>
        IEnumerable<ArtistData> BrowseArtists(int offset = 0, int limit = 4000, bool albumArtists = false);

        /// <summary>
        ///     Browses all albums in the library.
        ///     Returns List because deduplication requires Distinct operation.
        /// </summary>
        /// <param name="offset">Number of albums to skip</param>
        /// <param name="limit">Maximum number of albums to return</param>
        List<AlbumData> BrowseAlbums(int offset = 0, int limit = 4000);

        /// <summary>
        ///     Browses all tracks in the library.
        ///     Uses yield return for streaming results and proper query cleanup.
        /// </summary>
        /// <param name="offset">Number of tracks to skip</param>
        /// <param name="limit">Maximum number of tracks to return</param>
        IEnumerable<Track> BrowseTracks(int offset = 0, int limit = 4000);

        // Hierarchical Navigation

        /// <summary>
        ///     Gets all tracks for a specific album.
        ///     Uses yield return for streaming results and proper query cleanup.
        /// </summary>
        /// <param name="album">Album name</param>
        /// <param name="searchSource">Source to search in</param>
        IEnumerable<Track> GetAlbumTracks(string album, SearchSource searchSource);

        /// <summary>
        ///     Gets all albums for a specific artist.
        ///     Returns List because deduplication requires Contains/IndexOf operations.
        /// </summary>
        /// <param name="artist">Artist name</param>
        /// <param name="searchSource">Source to search in</param>
        List<AlbumData> GetArtistAlbums(string artist, SearchSource searchSource);

        /// <summary>
        ///     Gets all artists for a specific genre.
        ///     Uses yield return for streaming results and proper query cleanup.
        /// </summary>
        /// <param name="genre">Genre name</param>
        /// <param name="searchSource">Source to search in</param>
        IEnumerable<ArtistData> GetGenreArtists(string genre, SearchSource searchSource);

        // Artwork

        /// <summary>
        ///     Gets raw artwork data for a track (the Rust core resizes/caches it).
        /// </summary>
        /// <param name="trackPath">Track file path</param>
        /// <returns>Artwork byte array</returns>
        byte[] GetArtworkDataForTrack(string trackPath);

        // Album Cover Cache Support

        /// <summary>
        ///     Single-pass album identity scan feeding the Rust core's cover cache.
        ///     One library scan, folded to one entry per album (album artist +
        ///     album, case-insensitively): the representative track path (artwork
        ///     source) and the newest track modification time as unix seconds. The
        ///     core derives the cache key by hashing artist+album, so this does no
        ///     hashing. Replaces the old three-call
        ///     <see cref="GetAllAlbumIdentifiers" /> + <see cref="GetTrackPaths" /> +
        ///     <see cref="GetFileModificationDates" /> sequence (which scanned the
        ///     library 2-3 times).
        /// </summary>
        /// <returns>One (Artist, Album, Path, Modified) per unique album.</returns>
        List<(string Artist, string Album, string Path, long Modified)> GetAlbumIdentities();

        // Track Metadata

        /// <summary>
        ///     Gets album artist and album name for multiple tracks in a batch.
        /// </summary>
        /// <param name="trackPaths">Collection of track paths</param>
        /// <returns>Dictionary mapping track path to (Artist, Album) tuple</returns>
        Dictionary<string, (string Artist, string Album)> GetBatchTrackMetadata(IEnumerable<string> trackPaths);

        // Library Cache (MBRCIP-0001)

        /// <summary>
        ///     Every track path in library browse order, in a single
        ///     Library_QueryFilesEx call (no tags). This is the Rust core's ordinal
        ///     path index (its source of truth for browse order). The order must
        ///     match <see cref="BrowseTracks" /> so the core serves the same order
        ///     it always has.
        /// </summary>
        /// <returns>All track paths, in browse order.</returns>
        List<string> GetAllTrackPaths();

        /// <summary>
        ///     Reads the 7 browse tags (artist, title, album, album artist, genre,
        ///     track no, disc) for each given path - one bulk Library_GetFileTags
        ///     per path. The core calls this for a single browse page's slice only,
        ///     to fill its path-keyed tag cache lazily (never the whole library).
        /// </summary>
        /// <param name="paths">The page's track paths.</param>
        /// <returns>One Track per path, in the input order.</returns>
        List<Track> GetTracksForPaths(IEnumerable<string> paths);

        /// <summary>
        ///     Read the V6 typed-track tags for a page's paths: the base browse
        ///     fields plus the raw extended tags (Year/Duration/Rating/DateAdded)
        ///     the core parses into the typed V6 `track` schema. DateAdded is
        ///     converted to ISO-8601 UTC here (the core has no locale context).
        /// </summary>
        /// <param name="paths">The page's track paths.</param>
        /// <returns>One TrackTags per path, in the input order.</returns>
        List<TrackTags> GetTrackTags(IEnumerable<string> paths);

        /// <summary>
        ///     Library changes (Music category) since <paramref name="updatedSince" />
        ///     unix seconds, for the core's incremental scan. Deleted detection needs
        ///     the full cached-paths set, which the core does not marshal back, so
        ///     Deleted is left empty here - the core derives deletes from diffing the
        ///     re-fetched path index. Returns all-empty when the running MusicBee
        ///     build does not provide Library_GetSyncDelta.
        /// </summary>
        /// <param name="updatedSince">Watermark in unix seconds.</param>
        /// <returns>(Added, Updated, Deleted) track paths.</returns>
        (List<string> Added, List<string> Updated, List<string> Deleted) GetSyncDelta(long updatedSince);
    }
}
