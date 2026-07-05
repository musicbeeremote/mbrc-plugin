using System.Collections.Generic;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Models.Entities;

namespace MusicBeeRemote.Core.Tests.Mocks
{
    /// <summary>
    ///     Mock implementation of IPlaylistDataProvider for testing.
    ///     Allows verification of method calls and configuration of return values.
    /// </summary>
    public class MockPlaylistDataProvider : IPlaylistDataProvider
    {
        // Configurable state
        public List<Playlist> Playlists { get; set; } = new List<Playlist>();
        public List<string> NowPlayingFiles { get; set; } = new List<string>();

        // Call counters
        public int MoveTrackCallCount { get; private set; }
        public int RemoveTrackCallCount { get; private set; }
        public int PlayTrackCallCount { get; private set; }
        public int QueueFilesCallCount { get; private set; }
        public int PlayPlaylistCallCount { get; private set; }
        public int PlayAllLibraryCallCount { get; private set; }

        // Last call parameters
        public int LastMoveFromIndex { get; private set; }
        public int LastMoveToIndex { get; private set; }
        public int LastRemovedIndex { get; private set; }
        public QueueType LastQueueType { get; private set; }
        public string[] LastQueuedFiles { get; private set; }

        public MockPlaylistDataProvider()
        {
            // Add some default playlists
            Playlists.Add(new Playlist { Name = "Favorites", Url = "playlist://favorites" });
            Playlists.Add(new Playlist { Name = "Recently Played", Url = "playlist://recent" });
        }

        #region Now Playing List Management

        public bool MoveNowPlayingTrack(int fromIndex, int toIndex)
        {
            MoveTrackCallCount++;
            LastMoveFromIndex = fromIndex;
            LastMoveToIndex = toIndex;
            return true;
        }

        public bool RemoveFromNowPlayingList(int index)
        {
            RemoveTrackCallCount++;
            LastRemovedIndex = index;
            if (index >= 0 && index < NowPlayingFiles.Count)
            {
                NowPlayingFiles.RemoveAt(index);
                return true;
            }
            return false;
        }

        public bool PlayNowPlayingTrack(string fileUrl)
        {
            PlayTrackCallCount++;
            return true;
        }

        public bool PlayNowPlayingByIndex(int index)
        {
            PlayTrackCallCount++;
            return index >= 0 && index < NowPlayingFiles.Count;
        }

        #endregion

        #region Queue Operations

        public bool QueueFiles(QueueType queueType, string[] fileUrls, string playFileUrl = null)
        {
            QueueFilesCallCount++;
            LastQueueType = queueType;
            LastQueuedFiles = fileUrls;
            return true;
        }

        public bool PlayAllLibrary(bool shuffle = false)
        {
            PlayAllLibraryCallCount++;
            return true;
        }

        #endregion

        #region Playlist Management

        public bool PlayPlaylist(string playlistUrl)
        {
            PlayPlaylistCallCount++;
            return true;
        }

        public IEnumerable<Playlist> GetPlaylists() => Playlists;

        #endregion
    }
}
