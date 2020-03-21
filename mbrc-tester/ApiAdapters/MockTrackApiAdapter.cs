using System;
using System.Globalization;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Enumerations;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Monitoring;

namespace MbrcTester.ApiAdapters
{
    public class TrackApiAdapter : ITrackApiAdapter
    {
        private readonly MockPlayer _player;

        public TrackApiAdapter(MockPlayer player)
        {
            _player = player;
        }

        public TrackTemporalnformation GetTemporalInformation()
        {
           return _player.GetTemporalInformation();
        }

        public bool SeekTo(int position)
        {
            return _player.SeekTo(position);
        }

        public string GetLyrics()
        {
            throw new System.NotImplementedException();
        }

        public string GetCover()
        {
            throw new System.NotImplementedException();
        }

        public NowPlayingTrack GetPlayingTrackInfoLegacy()
        {
            var track = _player.PlayingTrack;
            return new NowPlayingTrack()
            {
                Album = track.Album,
                Artist = track.Artist,
                Title = track.Title,
                Year = track.Year
            };
        }

        public NowPlayingTrackV2 GetPlayingTrackInfo()
        {
            var track = _player.PlayingTrack;
            return new NowPlayingTrackV2()
            {
                Path = track._id,
                Album = track.Album,
                Artist = track.Artist,
                Title = track.Title,
                Year = track.Year
            };
        }

        public NowPlayingDetails GetPlayingTrackDetails()
        {
            var track = _player.PlayingTrack;
            return new NowPlayingDetails()
            {
                AlbumArtist = track.AlbumArtist,
                Genre = track.Genre,
                TrackNo = track.TrackNo.ToString(),
                DiscNo = track.Disc.ToString()
            };
        }

        public string SetRating(string rating)
        {
            return _player.SetRating(rating);
        }

        public string GetRating()
        {
            return _player.GetRating();
        }

        public LastfmStatus ChangeStatus(string action)
        {
            throw new System.NotImplementedException();
        }

        public LastfmStatus GetLfmStatus()
        {
            return LastfmStatus.Normal;
        }
    }
}