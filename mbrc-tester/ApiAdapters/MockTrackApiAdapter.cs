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

        public TrackApiAdapter()
        {
 
        }

        public TrackTemporalnformation GetTemporalInformation()
        {
            throw new System.NotImplementedException();
        }

        public bool SeekTo(int position)
        {
            throw new System.NotImplementedException();
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
            throw new System.NotImplementedException();
        }

        public NowPlayingTrackV2 GetPlayingTrackInfo()
        {
            throw new System.NotImplementedException();
        }

        public string SetRating(string rating)
        {
            throw new System.NotImplementedException();
        }

        public string GetRating()
        {
            throw new System.NotImplementedException();
        }

        public LastfmStatus ChangeStatus(string action)
        {
            throw new System.NotImplementedException();
        }

        private LastfmStatus SetLfmNormalStatus()
        {
            throw new System.NotImplementedException();
        }

        private LastfmStatus SetLfmLoveStatus()
        {
            throw new System.NotImplementedException();
        }

        private LastfmStatus SetLfmLoveBan()
        {
            throw new System.NotImplementedException();
        }

        public LastfmStatus GetLfmStatus()
        {
            throw new System.NotImplementedException();
        }

    }
}