using System.Net;
using MusicBeePlugin.AndroidRemote.Events;
using MusicBeePlugin.AndroidRemote.Model.Entities;
using MusicBeePlugin.AndroidRemote.Networking;
using TinyMessenger;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    internal class PluginResponseAvailableEvent : ITinyMessage
    {
        public SocketMessage Message { get; }
        public string ConnectionId { get; }

        public PluginResponseAvailableEvent(SocketMessage message, string connectionId = "all")
        {
            Message = message;
            ConnectionId = connectionId;
        }

        public object Sender { get; } = null;
    }

    internal class StartSocketServerEvent : ITinyMessage
    {
        public object Sender { get; } = null;
    }

    internal class StartServiceBroadcastEvent : ITinyMessage
    {
        public object Sender { get; } = null;
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

    internal class ClientDisconnectedEvent : ITinyMessage
    {
        public ClientDisconnectedEvent(string connectionId)
        {
            ConnectionId = connectionId;
        }

        public string ConnectionId { get; }
        public object Sender { get; } = null;
    }

    internal class ClientConnectedEvent : ITinyMessage
    {
        public IPAddress IpAddress { get; }
        public string ConnectionId { get; }

        public ClientConnectedEvent(IPAddress ipAddress, string connectionId)
        {
            IpAddress = ipAddress;
            ConnectionId = connectionId;
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

    internal class LyricsAvailable : ITinyMessage
    {
        public string Lyrics { get; }

        public LyricsAvailable(string lyrics)
        {
            Lyrics = lyrics;
        }

        public object Sender { get; } = null;
    }

    internal class CoverAvailable : ITinyMessage
    {
        public string Cover { get; }

        public CoverAvailable(string cover)
        {
            Cover = cover;
        }

        public object Sender { get; } = null;
    }
}