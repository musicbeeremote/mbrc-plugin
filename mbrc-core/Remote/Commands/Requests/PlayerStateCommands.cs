using MusicBeeRemoteCore.Remote.Interfaces;
using MusicBeeRemoteCore.Remote.Utilities;

namespace MusicBeeRemoteCore.Remote.Commands.Requests
{
    internal class RequestPlayerStatus : ICommand
    {
        public void Execute(IEvent @event)
        {
            Plugin.Instance.RequestPlayerStatus(@event.ConnectionId);
        }
    }

    internal class RequestPreviousTrack : LimitedCommand
    {
        public override void Execute(IEvent @event)
        {
            Plugin.Instance.RequestPreviousTrack(@event.ConnectionId);
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.PlayPrevious;
    }

    internal class RequestNextTrack : LimitedCommand
    {
        public override void Execute(IEvent @event)
        {
            Plugin.Instance.RequestNextTrack(@event.ConnectionId);
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
        public override void Execute(IEvent @event)
        {
            Plugin.Instance.RequestPlay(@event.ConnectionId);
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.StartPlayback;
    }

    internal class RequestPause : LimitedCommand
    {
        public override void Execute(IEvent @event)
        {
            Plugin.Instance.RequestPausePlayback(@event.ConnectionId);
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.StopPlayback;
    }

    internal class RequestPlayPause : LimitedCommand
    {
        public override void Execute(IEvent @event)
        {
            Plugin.Instance.RequestPlayPauseTrack(@event.ConnectionId);
        }

        public override CommandPermissions GetPermissions() => CommandPermissions.StartPlayback | CommandPermissions.StopPlayback;
    }

    internal class RequestStop : LimitedCommand
    {
        public override void Execute(IEvent @event)
        {
            Plugin.Instance.RequestStopPlayback(@event.ConnectionId);
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