using MusicBeeRemote.Core.Network.Http;

namespace MusicBeeRemote.Core.Network
{
    public interface IPluginNetworking
    {
        void Start();

        void Terminate();
    }

    public class PluginNetworking : IPluginNetworking
    {
        private readonly SocketServer _socketServer;
        private readonly ServiceDiscovery _serviceDiscovery;
        private readonly HttpSupport _httpSupport;
        private readonly ClientManager _clientManager;

        public PluginNetworking(
            SocketServer socketServer,
            ServiceDiscovery serviceDiscovery,
            HttpSupport httpSupport,
            ClientManager clientManager)
        {
            _socketServer = socketServer;
            _serviceDiscovery = serviceDiscovery;
            _httpSupport = httpSupport;
            _clientManager = clientManager;
        }

        /// <inheritdoc />
        public void Start()
        {
            _serviceDiscovery.Start();
            _socketServer.Start();
#if DEBUG
            _httpSupport.Start();
#endif
        }

        /// <inheritdoc />
        public void Terminate()
        {
            _socketServer.Terminate();
            _serviceDiscovery.Terminate();
#if DEBUG
            _httpSupport.Terminate();
#endif
        }
    }
}
