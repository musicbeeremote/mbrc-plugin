using MusicBeePlugin.AndroidRemote.Events;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Settings;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    internal class ReplayAvailable : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(eEvent.Data + "\r\n", eEvent.ConnectionId);
        }
    }

    internal class StartSocketServer : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Start();
        }
    }

    internal class StartServiceBroadcast : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            ServiceDiscovery.Instance.Start();
        }
    }

    internal class SocketStatusChanged : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.UpdateWindowStatus((bool) eEvent.Data);
        }
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

    internal class RestartSocketCommand : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.RestartSocket();
        }
    }

    internal class ForceClientDisconnect : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.KickClient(eEvent.ConnectionId);
        }
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

    internal class ClientConnected : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Authenticator.AddClientOnConnect(eEvent.ConnectionId);
        }
    }

    internal class BroadcastEventAvailable : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var broadcastEvent = eEvent.Data as BroadcastEvent;
            if (broadcastEvent != null)
            {
                SocketServer.Instance.Broadcast(broadcastEvent);
            }
        }
    }

    internal class StopSocketServer : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Stop();
        }
    }
}