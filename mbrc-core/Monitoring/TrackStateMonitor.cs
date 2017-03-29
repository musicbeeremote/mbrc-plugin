using System.Timers;
using MusicBeeRemoteCore.Core.Events.Notifications;
using MusicBeeRemoteCore.Remote.Commands;
using MusicBeeRemoteCore.Remote.Commands.Internal;
using MusicBeeRemoteCore.Remote.Events;
using MusicBeeRemoteCore.Remote.Model;
using MusicBeeRemoteCore.Remote.Networking;
using TinyMessenger;

namespace MusicBeeRemoteCore.Monitoring
{
    class TrackStateMonitor : ITrackStateMonitor
    {
        private readonly ITinyMessengerHub _hub;

        private Timer _positionUpdateTimer;
        private TinyMessageSubscriptionToken _coverSubscription;
        private TinyMessageSubscriptionToken _lyricsSubscription;

        //todo track cover lyrics changes and track info changes and notify client when needed
        public TrackStateMonitor(ITinyMessengerHub hub)
        {
            _hub = hub;
        }

        public void Start()
        {
            _coverSubscription = _hub.Subscribe<CoverDataReadyEvent>(msg => BroadcastCover(msg.Cover));
            _lyricsSubscription = _hub.Subscribe<LyricsDataReadyEvent>(msg => BroadcastLyrics(msg.Lyrics));
            _hub.Subscribe<TrackChangedEvent>(_ =>
            {
                RequestNowPlayingTrackCover();
                RequestTrackRating(string.Empty, string.Empty);
                RequestLoveStatus("status", "all");
                RequestNowPlayingTrackLyrics();
                RequestPlayPosition("status");
                var broadcastEvent = new BroadcastEvent(Constants.NowPlayingTrack);
                broadcastEvent.AddPayload(Constants.V2, GetTrackInfo());
                broadcastEvent.AddPayload(Constants.V3, GetTrackInfoV2());
                _hub.Publish(new BroadcastEventAvailable(broadcastEvent));
            })

            _positionUpdateTimer = new Timer(20000);
            _positionUpdateTimer.Elapsed += PositionUpdateTimerOnElapsed;
            _positionUpdateTimer.Enabled = true;
        }

        public void Stop()
        {
            _hub.Unsubscribe<CoverDataReadyEvent>(_coverSubscription);
            _hub.Unsubscribe<LyricsDataReadyEvent>(_lyricsSubscription);

            _positionUpdateTimer.Enabled = false;
            _positionUpdateTimer.Stop();
            _positionUpdateTimer.Close();
        }

        private void PositionUpdateTimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            if (_api.Player_GetPlayState() == PlayState.Playing)
            {
                RequestPlayPosition("status");
            }
        }

        public void BroadcastCover(string cover)
        {
            var payload = new CoverPayload(cover, false);
            var broadcastEvent = new BroadcastEvent(Constants.NowPlayingCover);
            broadcastEvent.AddPayload(Constants.V2, cover);
            broadcastEvent.AddPayload(Constants.V3, payload);
            _hub.Publish(new BroadcastEventAvailable(broadcastEvent));
        }

        public void BroadcastLyrics(string lyrics)
        {
            var versionTwoData = !string.IsNullOrEmpty(lyrics) ? lyrics : "Lyrics Not Found";

            var lyricsPayload = new LyricsPayload(lyrics);

            var broadcastEvent = new BroadcastEvent(Constants.NowPlayingLyrics);
            broadcastEvent.AddPayload(Constants.V2, versionTwoData);
            broadcastEvent.AddPayload(Constants.V3, lyricsPayload);
            _hub.Publish(new BroadcastEventAvailable(broadcastEvent));
        }
    }
}