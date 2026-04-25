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
        ///     Gets a page of the now playing list — Android-v4 wire shape.
        ///     <c>Position</c> is page-relative (1-based), album/album_artist
        ///     left empty so the Rust DTO's skip_serializing_if drops them
        ///     from the wire (matches captured Android-v4 frames).
        /// </summary>
        IEnumerable<NowPlaying> GetNowPlayingListPage(int offset, int limit);

        /// <summary>
        ///     Gets the now playing list starting from the currently-playing
        ///     track — iOS-v4 wire shape. <c>Position</c> is the
        ///     MusicBee-internal queue index (so iOS clients can show "you're
        ///     on track 47 of 200"), and album/album_artist are populated.
        ///     Captured iOS-v4 traces send {"offset":0} and expect this
        ///     queue-absolute indexing.
        /// </summary>
        IEnumerable<NowPlaying> GetNowPlayingListOrdered(int offset, int limit);

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
