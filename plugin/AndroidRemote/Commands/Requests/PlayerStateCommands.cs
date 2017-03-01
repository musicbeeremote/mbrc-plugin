using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestPlayerStatus : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPlayerStatus(eEvent.ConnectionId);
        }
    }

    internal class RequestPreviousTrack : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPreviousTrack(eEvent.ConnectionId);
        }
    }

    internal class RequestNextTrack : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestNextTrack(eEvent.ConnectionId);
        }
    }

    internal class RequestRepeat : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestRepeatState(eEvent.Data.Equals("toggle") ? StateAction.Toggle : StateAction.State);
        }
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

    internal class RequestShuffle : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            var stateAction = eEvent.Data.Equals("toggle")
                ? StateAction.Toggle
                : StateAction.State;

            if (Authenticator.ClientProtocolMisMatch(eEvent.ConnectionId))
            {
                Plugin.Instance.RequestShuffleState(stateAction);
            }
            else
            {
                Plugin.Instance.RequestAutoDjShuffleState(stateAction);
            }
        }
    }

    internal class RequestPlay : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPlay(eEvent.ConnectionId);
        }
    }

    internal class RequestPause : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPausePlayback(eEvent.ConnectionId);
        }
    }

    internal class RequestPlayPause : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPlayPauseTrack(eEvent.ConnectionId);
        }
    }

    internal class RequestStop : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestStopPlayback(eEvent.ConnectionId);
        }
    }

    internal class RequestVolume : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            int iVolume;
            if (!int.TryParse(eEvent.DataToString(), out iVolume)) return;

            Plugin.Instance.RequestVolumeChange(iVolume);
        }
    }

    internal class RequestMute : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestMuteState(eEvent.Data.Equals("toggle") ? StateAction.Toggle : StateAction.State);
        }
    }

    internal class RequestAutoDj : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestAutoDjState(
                (string) eEvent.Data == "toggle" ? StateAction.Toggle : StateAction.State);
        }
    }
}