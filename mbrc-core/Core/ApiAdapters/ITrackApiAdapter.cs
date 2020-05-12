using MusicBeeRemote.Core.Enumerations;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Monitoring;

namespace MusicBeeRemote.Core.ApiAdapters
{
    public interface ITrackApiAdapter
    {
        TrackTemporalInformation GetTemporalInformation();

        bool SeekTo(int position);

        string GetLyrics();

        string GetCover();

        NowPlayingTrack GetPlayingTrackInfoLegacy();

        NowPlayingTrackV2 GetPlayingTrackInfo();

        NowPlayingDetails GetPlayingTrackDetails();

        string SetRating(string rating);

        string GetRating();

        LastfmStatus ChangeStatus(string action);

        LastfmStatus GetLfmStatus();
    }
}
