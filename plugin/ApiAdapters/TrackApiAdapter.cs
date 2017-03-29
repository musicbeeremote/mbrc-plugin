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

        public string GetLyrics()
        {
            var embeddedLyrics = _api.NowPlaying_GetLyrics();
            if (!string.IsNullOrEmpty(embeddedLyrics))
            {
                return embeddedLyrics;
            }

            return _api.ApiRevision >= 17 ? _api.NowPlaying_GetDownloadedLyrics() : string.Empty;
        }

        public string GetCover()
        {
            var embeddedArtwork = _api.NowPlaying_GetArtwork();

            if (!string.IsNullOrEmpty(embeddedArtwork))
            {
                return embeddedArtwork;
            }

            if (_api.ApiRevision < 17) return string.Empty;

            var apiData = _api.NowPlaying_GetDownloadedArtwork();
            return !string.IsNullOrEmpty(apiData) ? apiData : string.Empty;
        }
}