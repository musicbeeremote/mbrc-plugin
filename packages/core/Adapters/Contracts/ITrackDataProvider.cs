using System.Collections.Generic;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Models.Entities;

namespace MusicBeePlugin.Adapters.Contracts
{
    /// <summary>
    ///     Data provider interface for track and now-playing operations.
    ///     Returns clean domain objects with no MusicBee-specific knowledge.
    ///     All MusicBee API interaction stays in the implementation.
    ///     Methods returning IEnumerable use yield return for lazy evaluation and proper cleanup.
    /// </summary>
    public interface ITrackDataProvider
    {
        // Now Playing Track Info

        /// <summary>
        ///     Gets the file URL/path of the currently playing track.
        /// </summary>
        string GetNowPlayingFileUrl();

        /// <summary>
        ///     Gets basic info for the currently playing track.
        /// </summary>
        NowPlayingTrackV2 GetNowPlayingTrackInfo();

        /// <summary>
        ///     Gets detailed metadata for the currently playing track.
        /// </summary>
        NowPlayingDetails GetNowPlayingTrackDetails();

        /// <summary>
        ///     Gets the current playback position and total duration.
        /// </summary>
        PlaybackPosition GetPlaybackPosition();

        // Media Content

        /// <summary>
        ///     Gets the artwork for the currently playing track as a base64-encoded string or file path.
        /// </summary>
        string GetNowPlayingArtwork();

        /// <summary>
        ///     Gets downloaded artwork for the currently playing track.
        /// </summary>
        string GetNowPlayingDownloadedArtwork();

        /// <summary>
        ///     Gets lyrics for the currently playing track.
        /// </summary>
        string GetNowPlayingLyrics();

        // Now Playing List Operations

        /// <summary>
        ///     Searches the now playing list for a track matching the query.
        /// </summary>
        /// <param name="query">Search query string</param>
        /// <param name="searchSource">Source to search in</param>
        /// <returns>File URL of the matching track, or null if not found</returns>
        string SearchNowPlayingList(string query, SearchSource searchSource);

        /// <summary>
        ///     Gets the now playing list in legacy format (limited to 5000 tracks).
        ///     Uses yield return for streaming results and proper query cleanup.
        /// </summary>
        IEnumerable<NowPlayingListTrack> GetNowPlayingListLegacy();

        /// <summary>
        ///     Gets the now playing list starting from current track position.
        ///     Uses yield return for streaming results and proper query cleanup.
        /// </summary>
        /// <param name="offset">Not used in ordered retrieval (starts from current)</param>
        /// <param name="limit">Maximum number of tracks to return</param>
        IEnumerable<NowPlaying> GetNowPlayingListOrdered(int offset, int limit);

        /// <summary>
        ///     Gets a page of the now playing list.
        ///     Uses yield return for streaming results and proper query cleanup.
        /// </summary>
        /// <param name="offset">Number of tracks to skip</param>
        /// <param name="limit">Maximum number of tracks to return</param>
        IEnumerable<NowPlaying> GetNowPlayingListPage(int offset, int limit);

        // Rating Operations

        /// <summary>
        ///     Gets the rating of the currently playing track.
        /// </summary>
        string GetNowPlayingRating();

        /// <summary>
        ///     Sets the rating of the currently playing track.
        /// </summary>
        /// <param name="rating">Rating value (0-5 or empty string)</param>
        /// <returns>True if successful</returns>
        bool SetNowPlayingRating(string rating);

        // Last.fm Operations

        /// <summary>
        ///     Gets the Last.fm status of the currently playing track.
        /// </summary>
        LastfmStatus GetNowPlayingLastfmStatus();

        /// <summary>
        ///     Sets the Last.fm status of the currently playing track.
        /// </summary>
        /// <param name="status">Status to set</param>
        /// <returns>True if successful</returns>
        bool SetNowPlayingLastfmStatus(LastfmStatus status);

        // Tag Operations

        /// <summary>
        ///     Sets a tag value for a track.
        /// </summary>
        /// <param name="fileUrl">Track file URL</param>
        /// <param name="tagName">Tag name to modify</param>
        /// <param name="value">New value for the tag</param>
        /// <returns>True if successful</returns>
        bool SetTrackTag(string fileUrl, string tagName, string value);

        /// <summary>
        ///     Commits tag changes to a track file.
        /// </summary>
        /// <param name="fileUrl">Track file URL</param>
        /// <returns>True if successful</returns>
        bool CommitTrackTags(string fileUrl);
    }
}
