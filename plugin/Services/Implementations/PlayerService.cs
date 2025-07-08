using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.AndroidRemote.Utilities;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.Services.Implementations
{
    /// <summary>
    /// Player service implementation that delegates to the Plugin instance
    /// </summary>
    public class PlayerService : IPlayerService
    {
        public void NextTrack(string clientId)
        {
            Plugin.Instance.RequestNextTrack(clientId);
        }

        public void PreviousTrack(string clientId)
        {
            Plugin.Instance.RequestPreviousTrack(clientId);
        }

        public void PlayPauseTrack(string clientId)
        {
            Plugin.Instance.RequestPlayPauseTrack(clientId);
        }

        public void StopPlayback(string clientId)
        {
            Plugin.Instance.RequestStopPlayback(clientId);
        }

        public void Play(string clientId)
        {
            Plugin.Instance.RequestPlay(clientId);
        }

        public void Pause(string clientId)
        {
            Plugin.Instance.RequestPausePlayback(clientId);
        }

        public void SetVolume(int volume)
        {
            Plugin.Instance.RequestVolumeChange(volume);
        }

        public void ToggleMute(StateAction action)
        {
            Plugin.Instance.RequestMuteState(action);
        }

        public void SetShuffleState(StateAction action)
        {
            Plugin.Instance.RequestShuffleState(action);
        }

        public void SetAutoDjShuffleState(StateAction action)
        {
            Plugin.Instance.RequestAutoDjShuffleState(action);
        }

        public void SetRepeatState(StateAction action)
        {
            Plugin.Instance.RequestRepeatState(action);
        }

        public void SetScrobblerState(StateAction action)
        {
            Plugin.Instance.RequestScrobblerState(action);
        }

        public void SetAutoDjState(StateAction action)
        {
            Plugin.Instance.RequestAutoDjState(action);
        }

        public void SetPlayPosition(string request)
        {
            Plugin.Instance.RequestPlayPosition(request);
        }

        public void GetOutputDevice(string clientId)
        {
            Plugin.Instance.RequestOutputDevice(clientId);
        }

        public void SwitchOutputDevice(string outputDevice, string clientId)
        {
            Plugin.Instance.SwitchOutputDevice(outputDevice, clientId);
        }

        public void GetPlayerStatus(string clientId)
        {
            Plugin.Instance.RequestPlayerStatus(clientId);
        }
    }
}