using System.Collections.Generic;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Models.Entities;

namespace MusicBeePlugin.Adapters.Contracts
{
    /// <summary>
    ///     Data provider interface for playlist and queue operations.
    ///     Returns clean domain objects with no MusicBee-specific knowledge.
    ///     All MusicBee API interaction stays in the implementation.
    ///     Methods returning IEnumerable use yield return for lazy evaluation and proper cleanup.
    /// </summary>
    public interface IPlaylistDataProvider
    {
        // Now Playing List Management

        /// <summary>
        ///     Moves a track in the now playing list from one position to another.
        /// </summary>
        /// <param name="fromIndex">Source index</param>
        /// <param name="toIndex">Destination index</param>
        /// <returns>True if successful</returns>
        bool MoveNowPlayingTrack(int fromIndex, int toIndex);

        /// <summary>
        ///     Removes a track from the now playing list.
        /// </summary>
        /// <param name="index">Index of the track to remove</param>
        /// <returns>True if successful</returns>
        bool RemoveFromNowPlayingList(int index);

        /// <summary>
        ///     Plays a track in the now playing list by file URL.
        /// </summary>
        /// <param name="fileUrl">File URL of the track to play</param>
        /// <returns>True if successful</returns>
        bool PlayNowPlayingTrack(string fileUrl);

        /// <summary>
        ///     Plays a track in the now playing list by index.
        /// </summary>
        /// <param name="index">Index of the track to play</param>
        /// <returns>True if successful</returns>
        bool PlayNowPlayingByIndex(int index);

        // Queue Operations

        /// <summary>
        ///     Queues files to the now playing list.
        /// </summary>
        /// <param name="queueType">How to queue the files (Next, Last, PlayNow, AddAndPlay)</param>
        /// <param name="fileUrls">File URLs to queue</param>
        /// <param name="playFileUrl">Specific file to play (for AddAndPlay type)</param>
        /// <returns>True if successful</returns>
        bool QueueFiles(QueueType queueType, string[] fileUrls, string playFileUrl = null);

        /// <summary>
        ///     Plays all tracks in the library.
        /// </summary>
        /// <param name="shuffle">If true, plays shuffled</param>
        /// <returns>True if successful</returns>
        bool PlayAllLibrary(bool shuffle = false);

        // Playlist Management

        /// <summary>
        ///     Plays a playlist by URL.
        /// </summary>
        /// <param name="playlistUrl">URL/path of the playlist</param>
        /// <returns>True if successful</returns>
        bool PlayPlaylist(string playlistUrl);

        /// <summary>
        ///     Gets all available playlists.
        ///     Uses yield return for streaming results and proper query cleanup.
        /// </summary>
        IEnumerable<Playlist> GetPlaylists();
    }
}
