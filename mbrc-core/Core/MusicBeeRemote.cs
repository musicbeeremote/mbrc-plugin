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
            throw new System.NotImplementedException();
        }

        public void NotifyVolumeLevelChanged()
        {
            throw new System.NotImplementedException();
        }

        public void NotifyVolumeMuteChanged()
        {
            throw new System.NotImplementedException();
        }

        public void NotifyPlayStateChanged()
        {
            throw new System.NotImplementedException();
        }

        public void NotifyLyricsReady()
        {
            throw new System.NotImplementedException();
        }

        public void NotifyArtworkReady()
        {
            throw new System.NotImplementedException();
        }

        public void NotifyNowPlayingListChanged()
        {
            throw new System.NotImplementedException();
        }
    }
}