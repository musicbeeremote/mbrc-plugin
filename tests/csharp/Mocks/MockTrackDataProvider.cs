using System.Collections.Generic;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Models.Entities;

namespace MusicBeeRemote.Core.Tests.Mocks
{
    /// <summary>
    ///     Mock implementation of ITrackDataProvider for testing.
    ///     Allows verification of method calls and configuration of return values.
    /// </summary>
    public class MockTrackDataProvider : ITrackDataProvider
    {
        // Configurable state
        public string CurrentFileUrl { get; set; } = "/path/to/song.mp3";
        public NowPlayingTrackV2 CurrentTrackInfo { get; set; }
        public NowPlayingDetails CurrentTrackDetails { get; set; }
        public PlaybackPosition CurrentPlaybackPosition { get; set; } = new PlaybackPosition(30000, 180000);
        public string Artwork { get; set; } = "/path/to/artwork.jpg";
        public string DownloadedArtwork { get; set; } = "";
        public string Lyrics { get; set; } = "Test lyrics";
        public string CurrentRating { get; set; } = "4";
        public LastfmStatus CurrentLastfmStatus { get; set; } = LastfmStatus.Normal;

        public List<NowPlayingListTrack> LegacyNowPlayingList { get; set; } = new List<NowPlayingListTrack>();
        public List<NowPlaying> NowPlayingList { get; set; } = new List<NowPlaying>();

        // Call counters
        public int SetRatingCallCount { get; private set; }
        public int SetLastfmStatusCallCount { get; private set; }
        public int SetTrackTagCallCount { get; private set; }
        public int CommitTrackTagsCallCount { get; private set; }

        public MockTrackDataProvider()
        {
            CurrentTrackInfo = new NowPlayingTrackV2
            {
                Artist = "Test Artist",
                Title = "Test Title",
                Album = "Test Album",
                Year = "2024",
                Path = CurrentFileUrl
            };

            CurrentTrackDetails = new NowPlayingDetails
            {
                AlbumArtist = "Test Album Artist",
                Genre = "Rock",
                TrackNo = "1",
                TrackCount = "10"
            };
        }

        #region Now Playing Track Info

        public string GetNowPlayingFileUrl() => CurrentFileUrl;

        public NowPlayingTrackV2 GetNowPlayingTrackInfo() => CurrentTrackInfo;

        public NowPlayingDetails GetNowPlayingTrackDetails() => CurrentTrackDetails;

        public PlaybackPosition GetPlaybackPosition() => CurrentPlaybackPosition;

        #endregion

        #region Media Content

        public string GetNowPlayingArtwork() => Artwork;

        public string GetNowPlayingDownloadedArtwork() => DownloadedArtwork;

        public string GetNowPlayingLyrics() => Lyrics;

        #endregion

        #region Now Playing List Operations

        public string SearchNowPlayingList(string query, SearchSource searchSource)
        {
            foreach (var track in NowPlayingList)
            {
                if (track.Title.Contains(query) || track.Artist.Contains(query))
                    return track.Path;
            }
            return null;
        }

        public IEnumerable<NowPlayingListTrack> GetNowPlayingListLegacy() => LegacyNowPlayingList;

        public IEnumerable<NowPlaying> GetNowPlayingListOrdered(int offset, int limit) => NowPlayingList;

        public IEnumerable<NowPlaying> GetNowPlayingListPage(int offset, int limit) => NowPlayingList;

        #endregion

        #region Rating Operations

        public string GetNowPlayingRating() => CurrentRating;

        public bool SetNowPlayingRating(string rating)
        {
            SetRatingCallCount++;
            CurrentRating = rating;
            return true;
        }

        #endregion

        #region Last.fm Operations

        public LastfmStatus GetNowPlayingLastfmStatus() => CurrentLastfmStatus;

        public bool SetNowPlayingLastfmStatus(LastfmStatus status)
        {
            SetLastfmStatusCallCount++;
            CurrentLastfmStatus = status;
            return true;
        }

        #endregion

        #region Tag Operations

        public bool SetTrackTag(string fileUrl, string tagName, string value)
        {
            SetTrackTagCallCount++;
            return true;
        }

        public bool CommitTrackTags(string fileUrl)
        {
            CommitTrackTagsCallCount++;
            return true;
        }

        #endregion
    }
}
