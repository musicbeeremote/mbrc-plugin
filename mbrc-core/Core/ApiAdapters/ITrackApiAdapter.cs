using MusicBeeRemoteCore.Monitoring;
using MusicBeeRemoteCore.Remote.Enumerations;
using MusicBeeRemoteCore.Remote.Model.Entities;

namespace MusicBeeRemoteCore.Core.ApiAdapters
{
    public interface ITrackApiAdapter
    {
        TrackTemporalnformation GetTemporalInformation();

        bool SeekTo(int position);

        string GetLyrics();

        string GetCover();

        NowPlayingTrack GetPlayingTrackInfoLegacy();

        NowPlayingTrackV2 GetPlayingTrackInfo();

        string SetRating(string rating);

        string GetRating();

        LastfmStatus ChangeStatus(string action);

        LastfmStatus GetLfmStatus();
    }
}