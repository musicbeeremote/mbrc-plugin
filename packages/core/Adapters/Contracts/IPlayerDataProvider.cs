using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Models.Entities;

namespace MusicBeePlugin.Adapters.Contracts
{
    /// <summary>
    ///     Data provider interface for player operations.
    ///     Returns clean domain objects with no MusicBee-specific knowledge.
    ///     All MusicBee API interaction stays in the implementation.
    /// </summary>
    public interface IPlayerDataProvider
    {
        // State Queries

        /// <summary>
        ///     Gets the current play state.
        /// </summary>
        PlayState GetPlayState();

        /// <summary>
        ///     Gets the combined shuffle state (Off, Shuffle, or AutoDJ).
        /// </summary>
        ShuffleState GetShuffleState();

        /// <summary>
        ///     Gets the raw shuffle enabled state (not combined with AutoDJ).
        /// </summary>
        bool GetShuffle();

        /// <summary>
        ///     Gets the current repeat mode.
        /// </summary>
        RepeatMode GetRepeatMode();

        /// <summary>
        ///     Gets the current volume (0-100).
        /// </summary>
        int GetVolume();

        /// <summary>
        ///     Gets the mute state.
        /// </summary>
        bool GetMute();

        /// <summary>
        ///     Gets whether scrobbling is enabled.
        /// </summary>
        bool GetScrobbleEnabled();

        /// <summary>
        ///     Gets whether AutoDJ is enabled.
        /// </summary>
        bool GetAutoDjEnabled();

        /// <summary>
        ///     Gets the current playback position in milliseconds.
        /// </summary>
        int GetPosition();

        // Playback Control

        /// <summary>
        ///     Starts playback if not already playing.
        /// </summary>
        bool Play();

        /// <summary>
        ///     Pauses playback if currently playing.
        /// </summary>
        bool Pause();

        /// <summary>
        ///     Toggles play/pause state.
        /// </summary>
        bool PlayPause();

        /// <summary>
        ///     Stops playback.
        /// </summary>
        bool StopPlayback();

        /// <summary>
        ///     Plays the next track.
        /// </summary>
        bool PlayNext();

        /// <summary>
        ///     Plays the previous track.
        /// </summary>
        bool PlayPrevious();

        // Settings

        /// <summary>
        ///     Sets the volume (0-100).
        /// </summary>
        /// <param name="volume">Volume level 0-100</param>
        /// <returns>True if successful</returns>
        bool SetVolume(int volume);

        /// <summary>
        ///     Sets the mute state.
        /// </summary>
        /// <param name="mute">True to mute, false to unmute</param>
        /// <returns>True if successful</returns>
        bool SetMute(bool mute);

        /// <summary>
        ///     Sets the shuffle state.
        /// </summary>
        /// <param name="enabled">True to enable shuffle</param>
        /// <returns>True if successful</returns>
        bool SetShuffle(bool enabled);

        /// <summary>
        ///     Sets the repeat mode.
        /// </summary>
        /// <param name="mode">The repeat mode to set</param>
        /// <returns>True if successful</returns>
        bool SetRepeatMode(RepeatMode mode);

        /// <summary>
        ///     Sets the scrobble state.
        /// </summary>
        /// <param name="enabled">True to enable scrobbling</param>
        /// <returns>True if successful</returns>
        bool SetScrobble(bool enabled);

        /// <summary>
        ///     Sets AutoDJ mode enabled or disabled.
        /// </summary>
        /// <param name="enabled">True to start AutoDJ, false to stop</param>
        /// <returns>True if successful</returns>
        bool SetAutoDj(bool enabled);

        /// <summary>
        ///     Sets the playback position in milliseconds.
        /// </summary>
        /// <param name="position">Position in milliseconds</param>
        /// <returns>True if successful</returns>
        bool SetPosition(int position);

        // Composite Status

        /// <summary>
        ///     Gets a complete player status object.
        /// </summary>
        /// <param name="legacyShuffleFormat">If true, returns shuffle as boolean; otherwise returns ShuffleState</param>
        /// <returns>Player status containing all state information</returns>
        PlayerStatus GetPlayerStatus(bool legacyShuffleFormat);

        // Output Devices

        /// <summary>
        ///     Gets available output devices.
        /// </summary>
        OutputDevice GetOutputDevices();

        /// <summary>
        ///     Sets the active output device.
        /// </summary>
        /// <param name="deviceName">Device name to activate</param>
        /// <returns>True if successful</returns>
        bool SetOutputDevice(string deviceName);
    }
}
