using MusicBeePlugin.AndroidRemote;
using MusicBeePlugin.AndroidRemote.Controller;
using MusicBeePlugin.AndroidRemote.Core;
using MusicBeePlugin.AndroidRemote.Core.Monitor;
using MusicBeePlugin.AndroidRemote.Model;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.PartyMode.Core;
using MusicBeePlugin.PartyMode.Core.Model;
using StructureMap;

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
//                c.For<Controller>().Singleton();
//
//                c.For<SocketServer>().Singleton();
//                c.For<LyricCoverModel>().Singleton();
//                c.For<ServiceDiscovery>().Singleton();
//                c.For<PartyModeModel>().Singleton();
                c.For<PartyModeCommandHandler>().Singleton();
                c.For<ITrackRepository>().Use<TrackRepository>().Singleton();
                c.For<ILibraryScanner>().Use<LibraryScanner>().Singleton();
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