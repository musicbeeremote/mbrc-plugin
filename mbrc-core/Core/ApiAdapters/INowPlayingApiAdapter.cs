using System.Collections.Generic;
using MusicBeeRemote.Core.Model.Entities;

namespace MusicBeeRemote.Core.ApiAdapters
{
    public interface INowPlayingApiAdapter
    {
        /// <summary>
        /// Moves a track in the now Playing list from the initial position to the destination position.
        /// </summary>
        /// <param name="startPosition">The original position of the track.</param>
        /// <param name="endPosition">The destination position of the track.</param>
        /// <returns>True if the move operation was successful and false if not.</returns>
        bool MoveTrack(int startPosition, int endPosition);

        bool PlayIndex(int index);

        /// <summary>
        /// Plays a track in the now playing list by the supplied path.
        /// </summary>
        /// <param name="path">The path of the file to play.</param>
        /// <returns>True if the operation was successful and false if not.</returns>
        bool PlayPath(string path);

        bool RemoveIndex(int index);

        IEnumerable<NowPlayingTrackInfo> GetTracks(int offset = 0, int limit = 5000);

        IEnumerable<NowPlayingListTrack> GetTracksLegacy(int offset = 0, int limit = 5000);
    }
}
