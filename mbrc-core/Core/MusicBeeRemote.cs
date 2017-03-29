using MusicBeeRemoteCore.Core.Events.Notifications;
using MusicBeeRemoteCore.Core.Windows;
using MusicBeeRemoteCore.Remote.Networking;
using TinyMessenger;

namespace MusicBeeRemoteCore.Core
{
    public class MusicBeeRemote : IMusicBeeRemote
    {
        private readonly SocketServer _socketServer;
        private readonly ServiceDiscovery _serviceDiscovery;
        private readonly IWindowManager _windowManager;
        private readonly ITinyMessengerHub _hub;


        public MusicBeeRemote(
            SocketServer socketServer,
            ServiceDiscovery serviceDiscovery,
            IWindowManager windowManager,
            ITinyMessengerHub hub
        )
        {
            _socketServer = socketServer;
            _serviceDiscovery = serviceDiscovery;
            _windowManager = windowManager;
            _hub = hub;
        }

        public void Start()
        {
            _serviceDiscovery.Start();
            _socketServer.Start();
        }

        public void Stop()
        {
            _socketServer.Stop();
            _serviceDiscovery.Stop();
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
            _hub.Publish(new NowPlayingListChanged());
        }
    }
}