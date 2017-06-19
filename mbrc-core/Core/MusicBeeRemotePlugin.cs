using MusicBeeRemote.Core.Events.Notifications;
using MusicBeeRemote.Core.Monitoring;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Windows;
using TinyMessenger;

namespace MusicBeeRemote.Core
{
    public class MusicBeeRemotePlugin : IMusicBeeRemotePlugin
    {
        private readonly SocketServer _socketServer;
        private readonly ServiceDiscovery _serviceDiscovery;
        private readonly HttpSupport _httpSupport;
        private readonly ITrackStateMonitor _trackStateMonitor;
        private readonly IPlayerStateMonitor _playerStateMonitor;
        private readonly IWindowManager _windowManager;
        private readonly ITinyMessengerHub _hub;

        public MusicBeeRemotePlugin(
            SocketServer socketServer,
            ServiceDiscovery serviceDiscovery,
            HttpSupport httpSupport,
            ITrackStateMonitor trackStateMonitor,
            IPlayerStateMonitor playerStateMonitor,
            IWindowManager windowManager,
            ITinyMessengerHub hub
        )
        {
            _socketServer = socketServer;
            _serviceDiscovery = serviceDiscovery;
            _httpSupport = httpSupport;
            _trackStateMonitor = trackStateMonitor;
            _playerStateMonitor = playerStateMonitor;
            _windowManager = windowManager;
            _hub = hub;
        }

        public void Start()
        {
            _trackStateMonitor.Start();
            _playerStateMonitor.Start();
            _serviceDiscovery.Start();
            _socketServer.Start();
            _httpSupport.Start();
        }

        public void Stop()
        {
            _socketServer.Stop();
            _serviceDiscovery.Stop();
            _trackStateMonitor.Stop();
            _playerStateMonitor.Stop();
            _httpSupport.Stop();            
        }

        public void DisplayInfoWindow()
        {
            _windowManager.DisplayInfoWindow();
        }

        public void NotifyTrackChanged()
        {
            _hub.Publish(new TrackChangedEvent());
        }

        public void NotifyVolumeLevelChanged()
        {
            _hub.Publish(new VolumeLevelChangedEvent());
        }

        public void NotifyVolumeMuteChanged()
        {
            _hub.Publish(new VolumeMuteChangedEvent());
        }

        public void NotifyPlayStateChanged()
        {
            _hub.Publish(new PlayStateChangedEvent());
        }

        public void NotifyLyricsReady()
        {
            _hub.Publish(new LyricsReadyEvent());
        }

        public void NotifyArtworkReady()
        {
            _hub.Publish(new ArtworkReadyEvent());
        }

        public void NotifyNowPlayingListChanged()
        {
            _hub.Publish(new NowPlayingListChangedEvent());
        }
    }
}