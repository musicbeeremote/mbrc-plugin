using System.Collections.Generic;
using System.Net;
using Autofac;
using MusicBeePlugin.Networking;
using MusicBeePlugin.Networking.Discovery;
using MusicBeePlugin.Networking.Server;
using MusicBeePlugin.Networking.Testing;
using MusicBeePlugin.Protocol.Processing;
using MusicBeePlugin.Services.Core;
using MusicBeePlugin.Tools;
using MusicBeePlugin.Utilities.Network;

namespace MusicBeePlugin.Infrastructure.DependencyInjection
{
    /// <summary>
    ///     Autofac module for registering networking and protocol handling components.
    ///     Contains socket servers, protocol handlers, service discovery, and network utilities.
    /// </summary>
    public class NetworkingModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            // Register Broadcaster service
            builder.RegisterType<Broadcaster>()
                .As<IBroadcaster>()
                .SingleInstance();

            // Register SocketTester
            builder.RegisterType<SocketTester>()
                .As<ISocketTester>()
                .InstancePerDependency();

            // Register Authenticator
            builder.RegisterType<Authenticator>()
                .As<IAuthenticator>()
                .SingleInstance();

            // Register ProtocolCapabilities
            builder.RegisterType<ProtocolCapabilities>()
                .As<IProtocolCapabilities>()
                .SingleInstance();

            // Register ProtocolHandler
            builder.RegisterType<ProtocolHandler>()
                .As<IProtocolHandler>()
                .SingleInstance();

            // Register NetworkTools wrapper (assuming NetworkTools is static)
            builder.Register(c => new NetworkToolsWrapper())
                .As<INetworkTools>()
                .SingleInstance();

            // Register SocketServer with its dependencies
            builder.RegisterType<SocketServer>()
                .As<ISocketServer>()
                .SingleInstance();

            // Register ServiceDiscovery
            builder.RegisterType<ServiceDiscovery>()
                .As<IServiceDiscovery>()
                .SingleInstance();

            // Register NetworkingManager as the main entry point
            builder.RegisterType<NetworkingManager>()
                .As<INetworkingManager>()
                .SingleInstance();
        }
    }

    // Wrapper for static NetworkTools
    internal sealed class NetworkToolsWrapper : INetworkTools
    {
        public List<IPAddress> GetAddressList()
        {
            return NetworkTools.GetAddressList();
        }

        public List<string> GetPrivateAddressList()
        {
            return NetworkTools.GetPrivateAddressList();
        }

        public IPAddress GetSubnetMask(string address)
        {
            return NetworkTools.GetSubnetMask(address);
        }

        public IPAddress GetNetworkAddress(IPAddress address, IPAddress subnetMask)
        {
            return NetworkTools.GetNetworkAddress(address, subnetMask);
        }
    }
}
