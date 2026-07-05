using System.Collections.Generic;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Models.Entities;

namespace MusicBeePlugin.Adapters.Contracts
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

        // Search Operations

        /// <summary>
        ///     Searches for genres matching the query.
        ///     Uses yield return for streaming results and proper query cleanup.
        /// </summary>
        /// <param name="query">Search query</param>
        /// <param name="searchSource">Source to search in</param>
        IEnumerable<GenreData> SearchGenres(string query, SearchSource searchSource);

        /// <summary>
        ///     Searches for artists matching the query.
        ///     Uses yield return for streaming results and proper query cleanup.
        /// </summary>
        /// <param name="query">Search query</param>
        /// <param name="searchSource">Source to search in</param>
        IEnumerable<ArtistData> SearchArtists(string query, SearchSource searchSource);

        /// <summary>
        ///     Searches for albums matching the query.
        ///     Returns List because deduplication requires Contains/IndexOf operations.
        /// </summary>
        /// <param name="query">Search query</param>
        /// <param name="searchSource">Source to search in</param>
        List<AlbumData> SearchAlbums(string query, SearchSource searchSource);

        /// <summary>
        ///     Searches for tracks matching the query.
        ///     Uses yield return for streaming results and proper query cleanup.
        /// </summary>
        /// <param name="query">Search query</param>
        /// <param name="searchSource">Source to search in</param>
        IEnumerable<Track> SearchTracks(string query, SearchSource searchSource);

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

        // Queue Support

        /// <summary>
        ///     Gets file URLs for tracks matching a metadata tag query.
        ///     Files are sorted by album artist, album, disc, and track number.
        /// </summary>
        /// <param name="tag">Tag type to filter by</param>
        /// <param name="query">Query value for the tag</param>
        /// <param name="searchSource">Source to search in</param>
        string[] GetFileUrlsByMetaTag(MetaTag tag, string query, SearchSource searchSource);

        // Artwork

        /// <summary>
        ///     Gets artwork URL/path for a track.
        /// </summary>
        /// <param name="trackPath">Track file path</param>
        /// <returns>Artwork URL or path, or empty string if not found</returns>
        string GetArtworkForTrack(string trackPath);

        /// <summary>
        ///     Gets raw artwork data for a track.
        /// </summary>
        /// <param name="trackPath">Track file path</param>
        /// <returns>Artwork byte array</returns>
        byte[] GetArtworkDataForTrack(string trackPath);

        // Album Cover Cache Support

        /// <summary>
        ///     Gets all unique album identifiers (for cover cache initialization).
        /// </summary>
        /// <returns>Dictionary mapping cover identifier to "Artist|Album" string</returns>
        Dictionary<string, string> GetAllAlbumIdentifiers();

        /// <summary>
        ///     Gets track paths for each unique album (for cover cache).
        /// </summary>
        /// <returns>Dictionary mapping cover identifier to track path</returns>
        Dictionary<string, string> GetTrackPaths();

        /// <summary>
        ///     Gets file modification dates for albums (for cover cache invalidation).
        /// </summary>
        /// <returns>Dictionary mapping cover identifier to modification date</returns>
        Dictionary<string, string> GetFileModificationDates();

        // Track Metadata

        /// <summary>
        ///     Gets the album artist for a track.
        /// </summary>
        /// <param name="trackPath">Track file path</param>
        string GetAlbumArtistForTrack(string trackPath);

        /// <summary>
        ///     Gets the album name for a track.
        /// </summary>
        /// <param name="trackPath">Track file path</param>
        string GetAlbumForTrack(string trackPath);

        /// <summary>
        ///     Gets album artist and album name for multiple tracks in a batch.
        /// </summary>
        /// <param name="trackPaths">Collection of track paths</param>
        /// <returns>Dictionary mapping track path to (Artist, Album) tuple</returns>
        Dictionary<string, (string Artist, string Album)> GetBatchTrackMetadata(IEnumerable<string> trackPaths);
    }
}
