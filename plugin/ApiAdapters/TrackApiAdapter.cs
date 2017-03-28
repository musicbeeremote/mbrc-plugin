using MusicBeeRemoteCore.Core.ApiAdapters;
using MusicBeeRemoteCore.Monitoring;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.ApiAdapters
{
    public class TrackApiAdapter : ITrackApiAdapter
    {
        private readonly MusicBeeApiInterface _api;

        public TrackApiAdapter(MusicBeeApiInterface api)
        {
            _api = api;
        }

        public TrackTemporalnformation GetTemporalInformation()
        {
            var position = _api.Player_GetPosition();
            var duration = _api.NowPlaying_GetDuration();
            return new TrackTemporalnformation(position, duration);
        }

        public bool SeekTo(int position)
        {
            return _api.Player_SetPosition(position);
        }
    }
}