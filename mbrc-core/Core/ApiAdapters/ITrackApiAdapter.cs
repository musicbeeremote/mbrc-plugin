using MusicBeeRemoteCore.Monitoring;

namespace MusicBeeRemoteCore.ApiAdapters
{
    public interface ITrackApiAdapter
    {
        TrackTemporalnformation GetTemporalInformation();

        bool SeekTo(int position);
    }
}