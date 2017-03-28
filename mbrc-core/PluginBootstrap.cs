using MusicBeeRemoteCore.Core.ApiAdapters;
using MusicBeeRemoteCore.PartyMode.Core;
using MusicBeeRemoteCore.PartyMode.Core.Model;
using MusicBeeRemoteCore.Remote;
using MusicBeeRemoteCore.Remote.Controller;
using MusicBeeRemoteCore.Remote.Core;
using MusicBeeRemoteCore.Remote.Core.Monitor;
using MusicBeeRemoteCore.Remote.Model;
using MusicBeeRemoteCore.Remote.Networking;
using MusicBeeRemoteCore.Remote.Settings;
using MusicBeeRemoteCore.Remote.Utilities;
using StructureMap;
using TinyMessenger;

namespace MusicBeeRemoteCore
{
    public class PluginBootstrap
    {
        public static void Initialize(Container container, Plugin.MusicBeeApiInterface plugin)
        {
            container.Configure(c =>
            {
                c.For<ILibraryApiAdapter>().Use<LibraryApiAdapter>().Singleton();
                c.For<ILibraryDataAdapter>().Use<LibraryDataAdapter>().Singleton();
                c.For<Controller>().Use<Controller>().Singleton();

                c.For<SocketServer>().Use<SocketServer>().Singleton();
                c.For<LyricCoverModel>().Use<LyricCoverModel>().Singleton();
                c.For<ServiceDiscovery>().Use<ServiceDiscovery>().Singleton();
                c.For<UserSettings>().Use<UserSettings>().Singleton();
                c.For<IStorageLocationProvider>().Use<StorageLocationProvider>().Ctor<string>().Is(plugin.Setting_GetPersistentStoragePath()).Singleton();
                c.For<Authenticator>().Use<Authenticator>().Singleton();
                c.For<PartyModeModel>().Use<PartyModeModel>().Singleton();
                c.For<PartyModeCommandHandler>().Use<PartyModeCommandHandler>().Singleton();
                c.For<ITrackRepository>().Use<TrackRepository>().Singleton();
                c.For<ILibraryScanner>().Use<LibraryScanner>().Singleton();
                c.For<ITinyMessengerHub>().Use<TinyMessengerHub>().Singleton();
                c.For<Plugin.MusicBeeApiInterface>().Use(() => plugin);
            });

            var controller = container.GetInstance<Controller>();
            var socket = container.GetInstance<SocketServer>();
            var discovery = container.GetInstance<ServiceDiscovery>();
            discovery.Start();
            Configuration.Register(controller, container);
        }
    }
}