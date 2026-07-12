using System.Collections.Generic;
using MusicBeePlugin.Models;
using MusicBeePlugin.Ffi;

namespace MusicBeePlugin.Providers
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
        TrackInfo GetNowPlayingTrackInfo();

        /// <summary>
        ///     Gets detailed metadata for the currently playing track.
        /// </summary>
        TrackDetails GetNowPlayingTrackDetails();

        /// <summary>
        ///     Gets the current playback position and total duration.
        /// </summary>
        PlaybackPositionResponse GetPlaybackPosition();

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
        ///     Gets the now playing list window [offset, offset+limit), ordered from
        ///     the current track forward (iOS variant). Source-side paged: it walks
        ///     the list order but reads tags ONLY for the items inside the window, so
        ///     a page costs O(limit) tag reads, never the whole list. The now-playing
        ///     list is live (changes on every track/queue op), so it is never cached.
        /// </summary>
        /// <param name="offset">Number of ordered items to skip before the window</param>
        /// <param name="limit">Window size; &lt;= 0 means "the rest from offset"</param>
        IEnumerable<NowPlayingListTrack> GetNowPlayingListOrdered(int offset, int limit);

        /// <summary>
        ///     Gets the now playing list window [offset, offset+limit) in sequential
        ///     order (Android variant). Source-side paged: it advances the list cursor
        ///     but reads tags ONLY for the items inside the window - O(limit) tag
        ///     reads per page, never the whole list. Never cached (the list is live).
        /// </summary>
        /// <param name="offset">Number of tracks to skip before the window</param>
        /// <param name="limit">Window size; &lt;= 0 means "the rest from offset"</param>
        IEnumerable<NowPlayingListTrack> GetNowPlayingListPage(int offset, int limit);

        /// <summary>
        ///     The number of tracks in the now playing list, for a page's reported
        ///     total. Cheap - one paths-only query, no tag reads.
        /// </summary>
        int GetNowPlayingListCount();

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
