using MusicBeeRemoteCore.Monitoring;

namespace MusicBeeRemoteCore.Core.ApiAdapters
{
    public interface ITrackApiAdapter
    {
        TrackTemporalnformation GetTemporalInformation();

        bool SeekTo(int position);

        string GetLyrics();

        string GetCover();
    }
}