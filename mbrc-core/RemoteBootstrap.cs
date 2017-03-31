using MusicBeeRemoteCore.Core;
using MusicBeeRemoteCore.Core.ApiAdapters;
using MusicBeeRemoteCore.Core.Settings;
using MusicBeeRemoteCore.Core.Support;
using MusicBeeRemoteCore.Core.Windows;
using MusicBeeRemoteCore.Logging;
using MusicBeeRemoteCore.Monitoring;
using MusicBeeRemoteCore.PartyMode.Core;
using MusicBeeRemoteCore.PartyMode.Core.Model;
using MusicBeeRemoteCore.Remote;
using MusicBeeRemoteCore.Remote.Controller;
using MusicBeeRemoteCore.Remote.Core;
using MusicBeeRemoteCore.Remote.Core.Monitor;
using MusicBeeRemoteCore.Remote.Model;
using MusicBeeRemoteCore.Remote.Networking;
using MusicBeeRemoteCore.Remote.Utilities;
using NLog;
using StructureMap;
using TinyMessenger;

namespace MusicBeeRemoteCore
{
    public class RemoteBootstrap
    {
        private readonly Container _container;

        public RemoteBootstrap()
        {
            _container = new Container();
        }

        public IMusicBeeRemote RegisterDependencies(MusicBeeDependencies dependencies)
        {
            _container.Configure(c =>
            {
                c.For<ILibraryApiAdapter>().Use(() => dependencies.LibraryAdapter).Singleton();
                c.For<INowPlayingApiAdapter>().Use(() => dependencies.NowPlayingAdapter).Singleton();
                c.For<IOutputApiAdapter>().Use(() => dependencies.OutputAdapter).Singleton();
                c.For<IPlayerApiAdapter>().Use(() => dependencies.PlayerAdapter).Singleton();
                c.For<IQueueAdapter>().Use(() => dependencies.QueueAdapter).Singleton();
                c.For<ITrackApiAdapter>().Use(() => dependencies.TrackAdapter).Singleton();
                c.For<IInvokeHandler>().Use(() => dependencies.InvokeHandler).Singleton();

                c.For<ISearchApi>().Use<SearchApi>().Singleton();
                c.For<ISearchQueue>().Use<SearchQueue>().Singleton();

                c.For<IWindowManager>().Use<WindowManager>().Singleton();

                c.For<IPluginLogManager>().Use<PluginLogManager>().Singleton();
                c.For<IPlayerStateMonitor>().Use<PlayerStateMonitor>().Singleton();
                c.For<ITrackStateMonitor>().Use<TrackStateMonitor>().Singleton();

                c.For<Controller>().Use<Controller>().Singleton();

                c.For<SocketServer>().Use<SocketServer>().Singleton();
                c.For<LyricCoverModel>().Use<LyricCoverModel>().Singleton();
                c.For<ServiceDiscovery>().Use<ServiceDiscovery>().Singleton();

                c.For<PersistanceManager>().Use<PersistanceManager>().Singleton();
                c.For<IJsonSettingsFileManager>().Use<JsonSettingsFileManager>().Singleton();
                c.For<ILegacySettingsMigration>().Use<LegacySettingsMigration>().Singleton();

                c.For<IStorageLocationProvider>()
                    .Use<StorageLocationProvider>()
                    .Ctor<string>()
                    .Is(dependencies.BaseStoragePath)
                    .Singleton();
                c.For<Authenticator>().Use<Authenticator>().Singleton();
                c.For<PartyModeModel>().Use<PartyModeModel>().Singleton();
                c.For<PartyModeCommandHandler>().Use<PartyModeCommandHandler>().Singleton();
                c.For<ITrackRepository>().Use<TrackRepository>().Singleton();
                c.For<ILibraryScanner>().Use<LibraryScanner>().Singleton();
                c.For<ITinyMessengerHub>().Use<TinyMessengerHub>().Singleton();
                c.For<IMusicBeeRemote>().Use<MusicBeeRemote>().Singleton();
            });

            var controller = _container.GetInstance<Controller>();
            Configuration.Register(controller, _container);

            var logManager = _container.GetInstance<IPluginLogManager>();
#if DEBUG
            logManager.Initialize(LogLevel.Debug);
#else
            logManager.Initialize(LogLevel.Error);
#endif

            return _container.GetInstance<IMusicBeeRemote>();
        }
    }
}