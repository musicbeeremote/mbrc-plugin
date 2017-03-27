using MusicBeePlugin.AndroidRemote;
using MusicBeePlugin.AndroidRemote.Controller;
using MusicBeePlugin.AndroidRemote.Core;
using MusicBeePlugin.AndroidRemote.Core.Monitor;
using MusicBeePlugin.AndroidRemote.Model;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Settings;
using MusicBeePlugin.AndroidRemote.Utilities;
using MusicBeePlugin.PartyMode.Core;
using MusicBeePlugin.PartyMode.Core.Model;
using StructureMap;
using TinyMessenger;

namespace MusicBeePlugin
{
    public class PluginBootstrap
    {
        public static void Initialize(Container container, Plugin.MusicBeeApiInterface plugin)
        {
            container.Configure(c =>
            {
                c.For<IApiAdapter>().Use<ApiAdapter>().Singleton();
                c.For<ILibraryApiAdapter>().Use<LibraryApiAdapter>().Singleton();
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