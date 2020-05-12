using MusicBeeRemote.Core.Caching.Monitor;
using MusicBeeRemote.Core.Events.Notifications;
using MusicBeeRemote.Core.Monitoring;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Network.Http;
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
        private readonly ILibraryScanner _libraryScanner;
        private readonly IWindowManager _windowManager;
        private readonly ClientManager _clientManager;
        private readonly ITinyMessengerHub _hub;

        public MusicBeeRemotePlugin(
            SocketServer socketServer,
            ServiceDiscovery serviceDiscovery,
            HttpSupport httpSupport,
            ITrackStateMonitor trackStateMonitor,
            IPlayerStateMonitor playerStateMonitor,
            ILibraryScanner libraryScanner,
            IWindowManager windowManager,
            ClientManager clientManager,
            ITinyMessengerHub hub)
        {
            _socketServer = socketServer;
            _serviceDiscovery = serviceDiscovery;
            _httpSupport = httpSupport;
            _trackStateMonitor = trackStateMonitor;
            _playerStateMonitor = playerStateMonitor;
            _libraryScanner = libraryScanner;
            _windowManager = windowManager;
            _clientManager = clientManager;
            _hub = hub;
        }

        public void Start()
        {
            _trackStateMonitor.Start();
            _playerStateMonitor.Start();
            _serviceDiscovery.Start();
            _socketServer.Start();
            _libraryScanner.Start();
#if DEBUG
            _httpSupport.Start();
#endif
        }

        public void Terminate()
        {
            _socketServer.Terminate();
            _serviceDiscovery.Terminate();
            _trackStateMonitor.Terminate();
            _playerStateMonitor.Terminate();
            _libraryScanner.Terminate();

#if DEBUG
            _httpSupport.Terminate();
#endif
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

        public void DisplayPartyModeWindow()
        {
            _windowManager.DisplayPartyModeWindow();
        }
    }
}
