using System;
using System.Timers;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Internal;
using MusicBeeRemote.Core.Events.Notifications;
using MusicBeeRemote.Core.Model;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Model.Generators;
using MusicBeeRemote.Core.Network;
using TinyMessenger;

namespace MusicBeeRemote.Core.Monitoring
{
    internal class TrackStateMonitor : ITrackStateMonitor, System.IDisposable
    {
        private readonly ITinyMessengerHub _hub;
        private readonly ITrackApiAdapter _apiAdapter;

        private Timer _positionUpdateTimer;
        private TinyMessageSubscriptionToken _coverSubscription;
        private TinyMessageSubscriptionToken _lyricsSubscription;
        private bool _isDisposed;

        public TrackStateMonitor(ITinyMessengerHub hub, ITrackApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public void Start()
        {
            _coverSubscription = _hub.Subscribe<CoverDataReadyEvent>(msg => BroadcastCover(msg.Cover));
            _lyricsSubscription = _hub.Subscribe<LyricsDataReadyEvent>(msg => BroadcastLyrics(msg.Lyrics));
            _hub.Subscribe<TrackChangedEvent>(_ => { BroadcastTrackInfo(); });
            _hub.Subscribe<ArtworkReadyEvent>(_ => { _hub.Publish(new CoverAvailable(_apiAdapter.GetCover())); });
            _hub.Subscribe<LyricsReadyEvent>(_ => { _hub.Publish(new LyricsAvailable(_apiAdapter.GetLyrics())); });

            _positionUpdateTimer = new Timer(20000);
            _positionUpdateTimer.Elapsed += PositionUpdateTimerOnElapsed;
            _positionUpdateTimer.Enabled = true;
        }

        public void Terminate()
        {
            _hub.Unsubscribe<CoverDataReadyEvent>(_coverSubscription);
            _hub.Unsubscribe<LyricsDataReadyEvent>(_lyricsSubscription);

            _positionUpdateTimer.Enabled = false;
            _positionUpdateTimer.Stop();
            _positionUpdateTimer.Close();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
            {
                return;
            }

            if (disposing)
            {
                _positionUpdateTimer.Dispose();
                _coverSubscription.Dispose();
                _lyricsSubscription.Dispose();
            }

            _isDisposed = true;
        }

        private void BroadcastTrackInfo()
        {
            BroadcastLfmStatus();
            BroadcastTrackRating();
            BroadcastPosition();

            var broadcastEvent = new BroadcastEvent(Constants.NowPlayingTrack);
            broadcastEvent.AddPayload(Constants.V2, _apiAdapter.GetPlayingTrackInfoLegacy());
            broadcastEvent.AddPayload(Constants.V3, _apiAdapter.GetPlayingTrackInfo());

            _hub.Publish(new BroadcastEventAvailable(broadcastEvent));
            _hub.Publish(new CoverAvailable(_apiAdapter.GetCover()));
            _hub.Publish(new LyricsAvailable(_apiAdapter.GetLyrics()));
        }

        private void BroadcastLfmStatus()
        {
            var lfmStatus = _apiAdapter.GetLfmStatus();
            var message = new SocketMessage(Constants.NowPlayingLfmRating, lfmStatus);
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }

        private void PositionUpdateTimerOnElapsed(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            BroadcastPosition();
        }

        private void BroadcastTrackRating()
        {
            var rating = _apiAdapter.GetRating();
            var message = new SocketMessage(Constants.NowPlayingRating, rating);
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }

        private void BroadcastPosition()
        {
            var temporalInformation = _apiAdapter.GetTemporalInformation();
            var message = new SocketMessage(Constants.NowPlayingPosition, temporalInformation);
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }

        private void BroadcastCover(string cover)
        {
            var payload = CoverPayloadGenerator.Create(cover, false);
            var broadcastEvent = new BroadcastEvent(Constants.NowPlayingCover);
            broadcastEvent.AddPayload(Constants.V2, cover);
            broadcastEvent.AddPayload(Constants.V3, payload);
            _hub.Publish(new BroadcastEventAvailable(broadcastEvent));
        }

        private void BroadcastLyrics(string lyrics)
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
