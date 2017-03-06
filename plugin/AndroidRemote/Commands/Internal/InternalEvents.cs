using System.Net;
using MusicBeePlugin.AndroidRemote.Events;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Settings;
using MusicBeePlugin.AndroidRemote.Utilities;
using TinyMessenger;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    internal class ReplayAvailable : ITinyMessage
    {
        public string Message { get; }
        public string ConnectionId { get; }

        public ReplayAvailable(string message, string connectionId)
        {
            Message = message + "\r\n";
            ConnectionId = connectionId;
        }

        public object Sender { get; } = null;
    }

    internal class StartSocketServer : ITinyMessage
    {
        public object Sender { get; } = null;
    }

    internal class StartServiceBroadcast : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            ServiceDiscovery.Instance.Start();
        }
    }

    internal class SocketStatusChanged : ITinyMessage
    {
        public bool IsRunning { get; }

        //todo (window info should listen for this
        public SocketStatusChanged(bool isRunning)
        {
            IsRunning = isRunning;
        }

        public object Sender { get; } = null;
    }

    internal class ShowFirstRunDialogCommand : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            if (UserSettings.Instance.IsFirstRun())
            {
                Plugin.Instance.OpenInfoWindow();
            }
        }
    }

    internal class RestartSocketEvent : ITinyMessage
    {
        public object Sender { get; } = null;
    }

    internal class ForceClientDisconnect : ITinyMessage
    {
        public string ConnectionId { get; }

        public ForceClientDisconnect(string connectionId)
        {
            ConnectionId = connectionId;
        }

        public object Sender { get; } = null;
    }

    internal class InitializeModelStateCommand : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var plugin = Plugin.Instance;
            plugin.RequestNowPlayingTrackCover();
            plugin.RequestNowPlayingTrackLyrics();
        }
    }

    internal class ClientDisconnected : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Authenticator.RemoveClientOnDisconnect(eEvent.ConnectionId);
        }
    }

    internal class ClientConnected : ITinyMessage
    {
        public IPAddress IpAddress { get; }
        public string ConnectionId { get; }

        public ClientConnected(IPAddress ipAddress, string connectionId)
        {
            IpAddress = ipAddress;
            ConnectionId = connectionId;

            //todo pass this to authenticator
            //Authenticator.AddClientOnConnect(eEvent.ConnectionId, clientAddress);
        }

       public object Sender { get; } = null;
    }

    internal class BroadcastEventAvailable : ITinyMessage
    {
        public BroadcastEvent BroadcastEvent { get; }

        public BroadcastEventAvailable(BroadcastEvent broadcastEvent)
        {
            BroadcastEvent = broadcastEvent;
        }

        public object Sender { get; } = null;
    }

    internal class StopSocketServer : ITinyMessage
    {
        public object Sender { get; } = null;
    }

    internal class ConnectionReadyEvent : ITinyMessage
    {
        public SocketConnection Client { get; }

        public ConnectionReadyEvent(SocketConnection client)
        {
            Client = client;
        }

        public object Sender { get; } = null;
    }

    internal class ConnectionClosedEvent : ITinyMessage
    {
        public string ConnectionId { get; }

        public ConnectionClosedEvent(string connectionId)
        {
            ConnectionId = connectionId;
        }

        public object Sender { get; } = null;
    }
}