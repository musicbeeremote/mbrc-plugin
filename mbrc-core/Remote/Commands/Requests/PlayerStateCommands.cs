using MusicBeeRemoteCore.Core.ApiAdapters;
using MusicBeeRemoteCore.Remote.Commands.Internal;
using MusicBeeRemoteCore.Remote.Interfaces;
using MusicBeeRemoteCore.Remote.Model.Entities;
using MusicBeeRemoteCore.Remote.Networking;
using MusicBeeRemoteCore.Remote.Utilities;
using TinyMessenger;

namespace MusicBeeRemoteCore.Remote.Commands.Requests
{
    internal class RequestPlayerStatus : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerStateAdapter _stateAdapter;

        public RequestPlayerStatus(ITinyMessengerHub hub, IPlayerStateAdapter stateAdapter)
        {
            _hub = hub;
            _stateAdapter = stateAdapter;
        }

        public void Execute(IEvent @event)
        {
            var statusMessage = new SocketMessage(Constants.PlayerStatus, _stateAdapter.GetStatus());
            _hub.Publish(new PluginResponseAvailableEvent(statusMessage, @event.ConnectionId));
        }
    }

    internal class RequestPreviousTrack : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerStateAdapter _stateAdapter;

        public RequestPreviousTrack(ITinyMessengerHub hub, IPlayerStateAdapter stateAdapter)
        {
            _hub = hub;
            _stateAdapter = stateAdapter;
        }

        public override void Execute(IEvent @event)
        {
            var message = new SocketMessage(Constants.PlayerPrevious, _stateAdapter.PlayPrevious());
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.PlayPrevious;
    }

    internal class RequestNextTrack : LimitedCommand
    {
        private readonly IPlayerStateAdapter _stateAdapter;
        private readonly ITinyMessengerHub _hub;

        public RequestNextTrack(IPlayerStateAdapter stateAdapter, ITinyMessengerHub hub)
        {
            _stateAdapter = stateAdapter;
            _hub = hub;
        }

        public override void Execute(IEvent @event)
        {
            var message = new SocketMessage(Constants.PlayerNext, _stateAdapter.PlayNext());
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.PlayNext;
    }

    internal class RequestRepeat : LimitedCommand
    {
        public override void Execute(IEvent @event)
        {
            Plugin.Instance.RequestRepeatState(@event.Data.Equals("toggle") ? StateAction.Toggle : StateAction.State);
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.ChangeRepeat;
    }

    internal class RequestScrobble : ICommand
    {
        public void Execute(IEvent @event)
        {
            Plugin.Instance.RequestScrobblerState(@event.Data.Equals("toggle")
                ? StateAction.Toggle
                : StateAction.State);
        }
    }

    internal class RequestShuffle : LimitedCommand
    {
        private readonly Authenticator _auth;

        public RequestShuffle(Authenticator auth)
        {
            _auth = auth;
        }

        public override void Execute(IEvent @event)
        {
            var stateAction = @event.Data.Equals("toggle")
                ? StateAction.Toggle
                : StateAction.State;

            if (_auth.ClientProtocolMisMatch(@event.ConnectionId))
            {
                Plugin.Instance.RequestShuffleState(stateAction);
            }
            else
            {
                Plugin.Instance.RequestAutoDjShuffleState(stateAction);
            }
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.ChangeShuffle;
    }

    internal class RequestPlay : LimitedCommand
    {
        private readonly IPlayerStateAdapter _playerStateAdapter;

        public RequestPlay(IPlayerStateAdapter playerStateAdapter)
        {
            _playerStateAdapter = playerStateAdapter;
        }

        public override void Execute(IEvent @event)
        {
            _playerStateAdapter.Play();
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.StartPlayback;
    }

    internal class RequestPause : LimitedCommand
    {
        private readonly IPlayerStateAdapter _playerStateAdapter;
        public RequestPause(IPlayerStateAdapter playerStateAdapter)
        {
            _playerStateAdapter = playerStateAdapter;
        }

        public override void Execute(IEvent @event)
        {
            _playerStateAdapter.Pause();
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.StopPlayback;
    }

    internal class RequestPlayPause : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerStateAdapter _stateAdapter;

        public RequestPlayPause(ITinyMessengerHub hub, IPlayerStateAdapter stateAdapter)
        {
            _hub = hub;
            _stateAdapter = stateAdapter;
        }

        public override void Execute(IEvent @event)
        {
            var message = new SocketMessage(Constants.PlayerPause, _stateAdapter.PlayPause());
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.StartPlayback |
                                                               CommandPermissions.StopPlayback;
    }

    internal class RequestStop : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerStateAdapter _stateAdapter;

        public RequestStop(ITinyMessengerHub hub, IPlayerStateAdapter stateAdapter)
        {
            _hub = hub;
            _stateAdapter = stateAdapter;
        }

        public override void Execute(IEvent @event)
        {
            var message = new SocketMessage(Constants.PlayerStop, _stateAdapter.StopPlayback());
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.StopPlayback;
    }

    internal class RequestVolume : LimitedCommand
    {
        public override void Execute(IEvent @event)
        {
            int iVolume;
            if (!int.TryParse(@event.DataToString(), out iVolume)) return;

            Plugin.Instance.RequestVolumeChange(iVolume);
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.ChangeVolume;
    }

    internal class RequestMute : LimitedCommand
    {
        public override void Execute(IEvent @event)
        {
            Plugin.Instance.RequestMuteState(@event.Data.Equals("toggle") ? StateAction.Toggle : StateAction.State);
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.CanMute;
    }

    internal class RequestAutoDj : LimitedCommand
    {
        public override void Execute(IEvent @event)
        {
            Plugin.Instance.RequestAutoDjState(
                (string) @event.Data == "toggle" ? StateAction.Toggle : StateAction.State);
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.ChangeShuffle;
    }
}