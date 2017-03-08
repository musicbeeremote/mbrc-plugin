using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Utilities;
using static MusicBeePlugin.AndroidRemote.Commands.CommandPermissions;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestPlayerStatus : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPlayerStatus(eEvent.ConnectionId);
        }
    }

    internal class RequestPreviousTrack : LimitedCommand
    {
        public override void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPreviousTrack(eEvent.ConnectionId);
        }

        public override CommandPermissions GetPermissions() => PlayPrevious;
    }

    internal class RequestNextTrack : LimitedCommand
    {
        public override void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestNextTrack(eEvent.ConnectionId);
        }

        public override CommandPermissions GetPermissions() => PlayNext;
    }

    internal class RequestRepeat : LimitedCommand
    {
        public override void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestRepeatState(eEvent.Data.Equals("toggle") ? StateAction.Toggle : StateAction.State);
        }

        public override CommandPermissions GetPermissions() => ChangeRepeat;
    }

    internal class RequestScrobble : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestScrobblerState(eEvent.Data.Equals("toggle")
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

        public override void Execute(IEvent eEvent)
        {
            var stateAction = eEvent.Data.Equals("toggle")
                ? StateAction.Toggle
                : StateAction.State;

            if (_auth.ClientProtocolMisMatch(eEvent.ConnectionId))
            {
                Plugin.Instance.RequestShuffleState(stateAction);
            }
            else
            {
                Plugin.Instance.RequestAutoDjShuffleState(stateAction);
            }
        }

        public override CommandPermissions GetPermissions() => ChangeShuffle;
    }

    internal class RequestPlay : LimitedCommand
    {
        public override void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPlay(eEvent.ConnectionId);
        }

        public override CommandPermissions GetPermissions() => StartPlayback;
    }

    internal class RequestPause : LimitedCommand
    {
        public override void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPausePlayback(eEvent.ConnectionId);
        }

        public override CommandPermissions GetPermissions() => StopPlayback;
    }

    internal class RequestPlayPause : LimitedCommand
    {
        public override void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPlayPauseTrack(eEvent.ConnectionId);
        }

        public override CommandPermissions GetPermissions() => StartPlayback | StopPlayback;
    }

    internal class RequestStop : LimitedCommand
    {
        public override void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestStopPlayback(eEvent.ConnectionId);
        }

        public override CommandPermissions GetPermissions() => StopPlayback;
    }

    internal class RequestVolume : LimitedCommand
    {
        public override void Execute(IEvent eEvent)
        {
            int iVolume;
            if (!int.TryParse(eEvent.DataToString(), out iVolume)) return;

            Plugin.Instance.RequestVolumeChange(iVolume);
        }

        public override CommandPermissions GetPermissions() => ChangeVolume;
    }

    internal class RequestMute : LimitedCommand
    {
        public override void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestMuteState(eEvent.Data.Equals("toggle") ? StateAction.Toggle : StateAction.State);
        }

        public override CommandPermissions GetPermissions() => CanMute;
    }

    internal class RequestAutoDj : LimitedCommand
    {
        public override void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestAutoDjState(
                (string) eEvent.Data == "toggle" ? StateAction.Toggle : StateAction.State);
        }

        public override CommandPermissions GetPermissions() => ChangeShuffle;
    }
}