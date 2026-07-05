using System.Collections.Generic;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Models.Entities;

namespace MusicBeePlugin.DataProviders
{
    /// <summary>
    ///     Data provider implementation for playlist and queue operations.
    ///     Contains all MusicBee-specific API logic including queue management and playlist operations.
    /// </summary>
    public class PlaylistDataProvider : IPlaylistDataProvider
    {
        private readonly Plugin.MusicBeeApiInterface _api;

        public PlaylistDataProvider(Plugin.MusicBeeApiInterface api)
        {
            _api = api;
        }

        #region Now Playing List Management

        public bool MoveNowPlayingTrack(int fromIndex, int toIndex)
        {
            // Create array for source indices (MusicBee API expects array)
            int[] fromIndices = { fromIndex };

            // Calculate destination index based on direction of move
            // This matches the logic from the original Plugin.RequestNowPlayingMove
            int destinationIndex;
            if (fromIndex > toIndex)
                destinationIndex = toIndex - 1;
            else
                destinationIndex = toIndex;

            // Call MusicBee API to move the track
            return _api.NowPlayingList_MoveFiles(fromIndices, destinationIndex);
        }

        public bool RemoveFromNowPlayingList(int index)
        {
            return _api.NowPlayingList_RemoveAt(index);
        }

        public bool PlayNowPlayingTrack(string fileUrl)
        {
            return _api.NowPlayingList_PlayNow(fileUrl);
        }

        public bool PlayNowPlayingByIndex(int index)
        {
            _api.NowPlayingList_QueryFiles(null);
            var trackToPlay = _api.NowPlayingList_GetListFileUrl(index);
            if (!string.IsNullOrEmpty(trackToPlay))
                return _api.NowPlayingList_PlayNow(trackToPlay);
            return false;
        }

        #endregion

        #region Queue Operations

        public bool QueueFiles(QueueType queueType, string[] fileUrls, string playFileUrl = null)
        {
            switch (queueType)
            {
                case QueueType.Next:
                    return _api.NowPlayingList_QueueFilesNext(fileUrls);

                case QueueType.Last:
                    return _api.NowPlayingList_QueueFilesLast(fileUrls);

                case QueueType.PlayNow:
                    _api.NowPlayingList_Clear();
                    _api.NowPlayingList_QueueFilesLast(fileUrls);
                    return _api.NowPlayingList_PlayNow(fileUrls[0]);

                case QueueType.AddAndPlay:
                    _api.NowPlayingList_Clear();
                    _api.NowPlayingList_QueueFilesLast(fileUrls);
                    return !string.IsNullOrEmpty(playFileUrl)
                        ? _api.NowPlayingList_PlayNow(playFileUrl)
                        : _api.NowPlayingList_PlayNow(fileUrls[0]);

                default:
                    return false;
            }
        }

        public bool PlayAllLibrary(bool shuffle = false)
        {
            if (shuffle)
                return _api.NowPlayingList_PlayLibraryShuffled();

            // End AutoDJ if enabled
            if (_api.Player_GetAutoDjEnabled())
                _api.Player_EndAutoDj();

            // Set shuffle to false
            _api.Player_SetShuffle(false);

            // Get all library files
            string[] songsList = null;
            _api.Library_QueryFilesEx(null, out songsList);

            if (songsList != null && songsList.Length > 0)
            {
                // Clear now playing list and queue all files
                _api.NowPlayingList_Clear();
                var success = _api.NowPlayingList_QueueFilesNext(songsList);
                if (success)
                    _api.Player_PlayNextTrack();
                return success;
            }

            return false;
        }

        #endregion

        #region Playlist Management

        public bool PlayPlaylist(string playlistUrl)
        {
            return _api.Playlist_PlayNow(playlistUrl);
        }

        public IEnumerable<Playlist> GetPlaylists()
        {
            _api.Playlist_QueryPlaylists();

            while (true)
            {
                var url = _api.Playlist_QueryGetNextPlaylist();
                if (string.IsNullOrEmpty(url))
                    yield break;

                var name = _api.Playlist_GetName(url);
                yield return new Playlist
                {
                    Name = name,
                    Url = url
                };
            }
        }

        #endregion
    }
}
