using System.Collections.Generic;
using MusicBeeRemoteCore.Remote.Model.Entities;

namespace MusicBeeRemoteCore.Core.ApiAdapters
{
    public interface INowPlayingApiAdapter
    {
        /// <summary>
        /// Searchs the available metadata in the now playing list and plays the first track matching the
        /// query supplied.
        /// </summary>
        /// <param name="query">A string that will be used to filter the available tracks</param>
        /// <returns>True if it managed to play a track or false if it failed</returns>
        bool PlayMatchingTrack(string query);

        /// <summary>
        /// Moves a track in the now Playing list from the initial position to the destination position.
        /// </summary>
        /// <param name="from">The original position of the track</param>
        /// <param name="to">The destination position of the track</param>
        /// <returns>True if the move operation was successful and false if not.</returns>
        bool MoveTrack(int from, int to);

        bool PlayIndex(int index);

        bool RemoveIndex(int index);

        IEnumerable<NowPlaying> GetTracks(int offset = 0, int limit = 5000);

        IEnumerable<NowPlayingListTrack> GetTracksLegacy(int offset = 0, int limit = 5000);
    }
}