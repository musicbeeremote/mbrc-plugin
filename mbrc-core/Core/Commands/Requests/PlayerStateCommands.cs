using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Utilities;
using Newtonsoft.Json.Linq;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands.Requests
{
    internal class RequestPlayerStatus : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestPlayerStatus(ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public void Execute(IEvent @event)
        {
            var statusMessage = new SocketMessage(Constants.PlayerStatus, _apiAdapter.GetStatus());
            _hub.Publish(new PluginResponseAvailableEvent(statusMessage, @event.ConnectionId));
        }
    }

    internal class RequestPreviousTrack : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestPreviousTrack(ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public override void Execute(IEvent @event)
        {
            var message = new SocketMessage(Constants.PlayerPrevious, _apiAdapter.PlayPrevious());
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.PlayPrevious;
    }

    internal class RequestNextTrack : LimitedCommand
    {
        private readonly IPlayerApiAdapter _apiAdapter;
        private readonly ITinyMessengerHub _hub;

        public RequestNextTrack(IPlayerApiAdapter apiAdapter, ITinyMessengerHub hub)
        {
            _apiAdapter = apiAdapter;
            _hub = hub;
        }

        public override void Execute(IEvent @event)
        {
            var message = new SocketMessage(Constants.PlayerNext, _apiAdapter.PlayNext());
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.PlayNext;
    }

    internal class RequestRepeat : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestRepeat(ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public override void Execute(IEvent @event)
        {
            var token = @event.Data as JToken;
            if (token != null && ((string) token).Equals("toggle"))
            {
                _apiAdapter.ToggleRepeatMode();
            }

            var message = new SocketMessage(Constants.PlayerRepeat, _apiAdapter.GetRepeatMode());
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.ChangeRepeat;
    }

    internal class RequestScrobble : ICommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestScrobble(ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public void Execute(IEvent @event)
        {
            var token = @event.Data as JToken;
            if (token != null && ((string) token).Equals("toggle"))
            {
                _apiAdapter.ToggleScrobbling();
            }
            var message = new SocketMessage(Constants.PlayerScrobble, _apiAdapter.ScrobblingEnabled());
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }
    }

    internal class RequestShuffle : LimitedCommand
    {
        private readonly Authenticator _auth;
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestShuffle(Authenticator auth, ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _auth = auth;
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public override void Execute(IEvent @event)
        {
            var isToggle = false;
            var token = @event.Data as JToken;
            if (token != null && ((string) token).Equals("toggle"))
            {
                isToggle = true;

            }

            SocketMessage message;
            if (_auth.ClientProtocolMisMatch(@event.ConnectionId))
            {
                if (isToggle)
                {
                    _apiAdapter.ToggleShuffleLegacy();
                }
                message = new SocketMessage(Constants.PlayerShuffle, _apiAdapter.GetShuffleLegacy());
            }
            else
            {
                var shuffleState = isToggle ? _apiAdapter.SwitchShuffle() : _apiAdapter.GetShuffleState();
                message = new SocketMessage(Constants.PlayerShuffle, shuffleState);
            }

            _hub.Publish(new PluginResponseAvailableEvent(message));
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.ChangeShuffle;
    }

    internal class RequestPlay : LimitedCommand
    {
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestPlay(IPlayerApiAdapter playerApiAdapter)
        {
            _apiAdapter = playerApiAdapter;
        }

        public override void Execute(IEvent @event)
        {
            _apiAdapter.Play();
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.StartPlayback;
    }

    internal class RequestPause : LimitedCommand
    {
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestPause(IPlayerApiAdapter playerApiAdapter)
        {
            _apiAdapter = playerApiAdapter;
        }

        public override void Execute(IEvent @event)
        {
            _apiAdapter.Pause();
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.StopPlayback;
    }

    internal class RequestPlayPause : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestPlayPause(ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public override void Execute(IEvent @event)
        {
            var message = new SocketMessage(Constants.PlayerPause, _apiAdapter.PlayPause());
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.StartPlayback |
                                                               CommandPermissions.StopPlayback;
    }

    internal class RequestStop : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestStop(ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public override void Execute(IEvent @event)
        {
            var message = new SocketMessage(Constants.PlayerStop, _apiAdapter.StopPlayback());
            _hub.Publish(new PluginResponseAvailableEvent(message, @event.ConnectionId));
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.StopPlayback;
    }

    internal class RequestVolume : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestVolume(ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public override void Execute(IEvent @event)
        {
            var token = @event.Data as JToken;
            if (token == null || token.Type != JTokenType.Integer)
            {
                return;
            }

            var newVolume = token.Value<int>();

            _apiAdapter.SetVolume(newVolume);

            var message = new SocketMessage(Constants.PlayerVolume, _apiAdapter.GetVolume());
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.ChangeVolume;
    }

    internal class RequestMute : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestMute(ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public override void Execute(IEvent @event)
        {
            var isToggle = false;
            var token = @event.Data as JToken;
            if (token != null && ((string) token).Equals("toggle"))
            {
                isToggle = true;

            }

            if (isToggle)
            {
                _apiAdapter.ToggleMute();
            }

            var message = new SocketMessage(Constants.PlayerMute, _apiAdapter.IsMuted());
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.CanMute;
    }

    internal class RequestAutoDj : LimitedCommand
    {
        private readonly ITinyMessengerHub _hub;
        private readonly IPlayerApiAdapter _apiAdapter;

        public RequestAutoDj(ITinyMessengerHub hub, IPlayerApiAdapter apiAdapter)
        {
            _hub = hub;
            _apiAdapter = apiAdapter;
        }

        public override void Execute(IEvent @event)
        {
            var isToggle = false;
            var token = @event.Data as JToken;
            if (token != null && ((string) token).Equals("toggle"))
            {
                isToggle = true;

            }

            if (isToggle)
            {
                _apiAdapter.ToggleAutoDjLegacy();
            }

            var message = new SocketMessage(Constants.PlayerAutoDj, _apiAdapter.IsAutoDjEnabledLegacy());
            _hub.Publish(new PluginResponseAvailableEvent(message));
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.ChangeShuffle;
    }
}