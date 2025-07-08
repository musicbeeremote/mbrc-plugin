using MusicBeePlugin.AndroidRemote.Enumerations;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.Services.Interfaces
{
    /// <summary>
    /// Service interface for player control operations
    /// </summary>
    public interface IPlayerService
    {
        /// <summary>
        /// Advances to the next track in the queue
        /// </summary>
        void NextTrack(string clientId);
        
        /// <summary>
        /// Goes back to the previous track in the queue
        /// </summary>
        void PreviousTrack(string clientId);
        
        /// <summary>
        /// Toggles play/pause state
        /// </summary>
        void PlayPauseTrack(string clientId);
        
        /// <summary>
        /// Stops playback
        /// </summary>
        void StopPlayback(string clientId);
        
        /// <summary>
        /// Starts playback
        /// </summary>
        void Play(string clientId);
        
        /// <summary>
        /// Pauses playback
        /// </summary>
        void Pause(string clientId);
        
        /// <summary>
        /// Sets the volume level
        /// </summary>
        void SetVolume(int volume);
        
        /// <summary>
        /// Toggles mute state
        /// </summary>
        void ToggleMute(StateAction action);
        
        /// <summary>
        /// Sets shuffle state
        /// </summary>
        void SetShuffleState(StateAction action);
        
        /// <summary>
        /// Sets auto DJ shuffle state
        /// </summary>
        void SetAutoDjShuffleState(StateAction action);
        
        /// <summary>
        /// Sets repeat state
        /// </summary>
        void SetRepeatState(StateAction action);
        
        /// <summary>
        /// Sets scrobbler state
        /// </summary>
        void SetScrobblerState(StateAction action);
        
        /// <summary>
        /// Sets auto DJ state
        /// </summary>
        void SetAutoDjState(StateAction action);
        
        /// <summary>
        /// Sets play position
        /// </summary>
        void SetPlayPosition(string request);
        
        /// <summary>
        /// Gets current output device
        /// </summary>
        void GetOutputDevice(string clientId);
        
        /// <summary>
        /// Switches to a different output device
        /// </summary>
        void SwitchOutputDevice(string outputDevice, string clientId);
        
        /// <summary>
        /// Gets current player status
        /// </summary>
        void GetPlayerStatus(string clientId);
    }
}