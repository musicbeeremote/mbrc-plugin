using MusicBeePlugin.AndroidRemote;
using MusicBeePlugin.AndroidRemote.Controller;
using MusicBeePlugin.AndroidRemote.Core;
using MusicBeePlugin.AndroidRemote.Model;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.PartyMode.Core;
using MusicBeePlugin.PartyMode.Core.Model;
using TinyIoC;

namespace MusicBeePlugin
{
    public class PluginBootstrap
    {
        public static void Initialize(TinyIoCContainer container, Plugin.MusicBeeApiInterface plugin)
        {
            container.Register<Controller>().AsSingleton();
            container.Register<SocketServer>().AsSingleton();
            container.Register<LyricCoverModel>().AsSingleton();
            container.Register<ServiceDiscovery>().AsSingleton();
            container.Register<PartyModeModel>().AsSingleton();
            container.Register<PartyModeCommandHandler>().AsSingleton();
            container.Register(plugin);
            container.Register<IApiAdapter, ApiAdapter>();
            container.Register<ILibraryApiAdapter, LibraryApiAdapter>().AsSingleton();
            var controller = container.Resolve<Controller>();
            var socket = container.Resolve<SocketServer>();
            var discovery = container.Resolve<ServiceDiscovery>();
            discovery.Start();
            Configuration.Register(controller, container);
        }
    }
}