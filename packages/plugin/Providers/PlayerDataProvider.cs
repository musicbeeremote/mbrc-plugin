using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MusicBeePlugin.Providers;
using MusicBeePlugin.Models;
using MusicBeePlugin.Ffi;

namespace MusicBeePlugin.Providers
{
    /// <summary>
    ///     Data provider implementation for player operations.
    ///     Contains all MusicBee-specific API logic including state mapping and type conversions.
    /// </summary>
    public class PlayerDataProvider : IPlayerDataProvider
    {
        private readonly Plugin.MusicBeeApiInterface _api;

        public PlayerDataProvider(Plugin.MusicBeeApiInterface api)
        {
            _api = api;
        }

        #region State Queries

        public PlayState GetPlayState()
        {
            var mbPlayState = _api.Player_GetPlayState();
            switch (mbPlayState)
            {
                case Plugin.PlayState.Playing:
                    return PlayState.Playing;
                case Plugin.PlayState.Paused:
                    return PlayState.Paused;
                case Plugin.PlayState.Stopped:
                    return PlayState.Stopped;
                case Plugin.PlayState.Undefined:
                case Plugin.PlayState.Loading:
                default:
                    return PlayState.Stopped;
            }
        }

        public ShuffleMode GetShuffleMode()
        {
            var autoDjEnabled = _api.Player_GetAutoDjEnabled();
            if (autoDjEnabled)
                return ShuffleMode.AutoDj;

            var shuffleEnabled = _api.Player_GetShuffle();
            return shuffleEnabled ? ShuffleMode.Shuffle : ShuffleMode.Off;
        }

        public RepeatMode GetRepeatMode()
        {
            var mbRepeat = _api.Player_GetRepeat();
            switch (mbRepeat)
            {
                case Plugin.RepeatMode.None:
                    return RepeatMode.None;
                case Plugin.RepeatMode.All:
                    return RepeatMode.All;
                case Plugin.RepeatMode.One:
                    return RepeatMode.One;
                default:
                    return RepeatMode.None;
            }
        }

        public int GetVolume()
        {
            return (int)Math.Round(_api.Player_GetVolume() * 100, 1);
        }

        public bool GetMute()
        {
            return _api.Player_GetMute();
        }

        public bool GetScrobbleEnabled()
        {
            return _api.Player_GetScrobbleEnabled();
        }

        public int GetPosition()
        {
            return _api.Player_GetPosition();
        }

        #endregion

        #region Playback Control

        public bool Play()
        {
            var currentState = _api.Player_GetPlayState();
            // Already playing - return true
            if (currentState == Plugin.PlayState.Playing)
                return true;
            return _api.Player_PlayPause();
        }

        public bool Pause()
        {
            var currentState = _api.Player_GetPlayState();
            // Already paused or stopped - return true
            if (currentState != Plugin.PlayState.Playing)
                return true;
            return _api.Player_PlayPause();
        }

        public bool PlayPause()
        {
            return _api.Player_PlayPause();
        }

        public bool StopPlayback()
        {
            return _api.Player_Stop();
        }

        public bool PlayNext()
        {
            return _api.Player_PlayNextTrack();
        }

        public bool PlayPrevious()
        {
            return _api.Player_PlayPreviousTrack();
        }

        #endregion

        #region Settings

        public bool SetVolume(int volume)
        {
            if (volume < 0 || volume > 100)
                return false;

            var success = _api.Player_SetVolume((float)volume / 100);

            // Unmute if currently muted
            if (_api.Player_GetMute())
            {
                var muteSuccess = _api.Player_SetMute(false);
                return success && muteSuccess;
            }

            return success;
        }

        public bool SetMute(bool mute)
        {
            return _api.Player_SetMute(mute);
        }

        public bool SetShuffle(bool enabled)
        {
            return _api.Player_SetShuffle(enabled);
        }

        public bool SetRepeatMode(RepeatMode mode)
        {
            Plugin.RepeatMode mbMode;
            switch (mode)
            {
                case RepeatMode.None:
                    mbMode = Plugin.RepeatMode.None;
                    break;
                case RepeatMode.All:
                    mbMode = Plugin.RepeatMode.All;
                    break;
                case RepeatMode.One:
                    mbMode = Plugin.RepeatMode.One;
                    break;
                default:
                    mbMode = Plugin.RepeatMode.None;
                    break;
            }

            return _api.Player_SetRepeat(mbMode);
        }

        public bool SetScrobble(bool enabled)
        {
            // Enabling scrobbling when no Last.fm account is configured makes
            // MusicBee pop a modal login dialog that blocks the plugin thread
            // (and thus all remote actions). Guard against it: only turn
            // scrobbling on when an account exists. Disabling is always safe.
            if (enabled && !HasLastFmAccount())
            {
                return false;
            }

            return _api.Player_SetScrobbleEnabled(enabled);
        }

        /// <summary>
        /// Whether a Last.fm account is configured in MusicBee. The API exposes
        /// no password/credential flag, so a non-empty user id is the proxy.
        /// <see cref="Plugin.MusicBeeApiInterface.Setting_GetLastFmUserId"/> may
        /// be unbound on older API revisions; when we can't tell, assume an
        /// account exists rather than block scrobbling outright.
        /// </summary>
        private bool HasLastFmAccount()
        {
            var getUserId = _api.Setting_GetLastFmUserId;
            return getUserId == null || !string.IsNullOrEmpty(getUserId());
        }

        public bool SetAutoDj(bool enabled)
        {
            if (enabled)
                return _api.Player_StartAutoDj();

            _api.Player_EndAutoDj();
            return true;
        }

        public bool SetPosition(int position)
        {
            return _api.Player_SetPosition(position);
        }

        #endregion

        #region Output Devices

        public OutputDevices GetOutputDevices()
        {
            var success = _api.Player_GetOutputDevices(out var deviceNames, out var activeDeviceName);
            return success
                ? new OutputDevices
                {
                    active = activeDeviceName ?? string.Empty,
                    devices = (deviceNames ?? Array.Empty<string>()).ToList(),
                }
                : new OutputDevices { active = string.Empty, devices = new List<string>() };
        }

        public bool SetOutputDevice(string deviceName)
        {
            return _api.Player_SetOutputDevice(deviceName);
        }

        #endregion
    }
}
