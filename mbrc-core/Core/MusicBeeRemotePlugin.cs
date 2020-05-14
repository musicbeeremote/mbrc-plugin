using MusicBeeRemote.Core.Caching.Monitor;
using MusicBeeRemote.Core.Events.Notifications;
using MusicBeeRemote.Core.Monitoring;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Windows;
using TinyMessenger;

namespace MusicBeeRemote.Core
{
    public class MusicBeeRemotePlugin : IMusicBeeRemotePlugin
    {
        private readonly ITrackStateMonitor _trackStateMonitor;
        private readonly IPlayerStateMonitor _playerStateMonitor;
        private readonly ILibraryScanner _libraryScanner;
        private readonly IPluginNetworking _pluginNetworking;
        private readonly IWindowManager _windowManager;
        private readonly ITinyMessengerHub _hub;

        public MusicBeeRemotePlugin(
            ITrackStateMonitor trackStateMonitor,
            IPlayerStateMonitor playerStateMonitor,
            ILibraryScanner libraryScanner,
            IPluginNetworking pluginNetworking,
            IWindowManager windowManager,
            ITinyMessengerHub hub)
        {
            _trackStateMonitor = trackStateMonitor;
            _playerStateMonitor = playerStateMonitor;
            _libraryScanner = libraryScanner;
            _pluginNetworking = pluginNetworking;
            _windowManager = windowManager;
            _hub = hub;
        }

        public void Start()
        {
            _trackStateMonitor.Start();
            _playerStateMonitor.Start();
            _libraryScanner.Start();
            _pluginNetworking.Start();
        }

        public void Terminate()
        {
            _trackStateMonitor.Terminate();
            _playerStateMonitor.Terminate();
            _libraryScanner.Terminate();
            _pluginNetworking.Terminate();
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
