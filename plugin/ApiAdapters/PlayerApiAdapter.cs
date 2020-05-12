using System;
using MusicBeePlugin.Properties;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Enumerations;
using MusicBeeRemote.Core.Model;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.ApiAdapters
{
    /// <inheritdoc />
    public class PlayerApiAdapter : IPlayerApiAdapter
    {
        private readonly MusicBeeApiInterface _api;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlayerApiAdapter"/> class.
        /// </summary>
        /// <param name="api">The MusicBee API.</param>
        public PlayerApiAdapter(MusicBeeApiInterface api)
        {
            _api = api;
        }

        /// <inheritdoc />
        public ShuffleState GetShuffleState()
        {
            var shuffleEnabled = _api.Player_GetShuffle();
            var autoDjEnabled = _api.Player_GetAutoDjEnabled();
            var state = ShuffleState.Off;
            if (shuffleEnabled && !autoDjEnabled)
            {
                state = ShuffleState.Shuffle;
            }
            else if (autoDjEnabled)
            {
                state = ShuffleState.Autodj;
            }

            return state;
        }

        /// <inheritdoc />
        public Repeat GetRepeatMode()
        {
            var repeat = _api.Player_GetRepeat();
            Repeat repeatMode;
            switch (repeat)
            {
                case RepeatMode.None:
                    repeatMode = Repeat.None;
                    break;
                case RepeatMode.All:
                    repeatMode = Repeat.All;
                    break;
                case RepeatMode.One:
                    repeatMode = Repeat.One;
                    break;
                default:
                    throw new Exception(Resources.InvalidRepeatMode);
            }

            return repeatMode;
        }

        /// <inheritdoc />
        public bool ToggleRepeatMode()
        {
            switch (_api.Player_GetRepeat())
            {
                case RepeatMode.None:
                    return _api.Player_SetRepeat(RepeatMode.All);
                case RepeatMode.All:
                    return _api.Player_SetRepeat(RepeatMode.None);
                case RepeatMode.One:
                    return _api.Player_SetRepeat(RepeatMode.None);
                default:
                    throw new Exception(Resources.InvalidRepeatMode);
            }
        }

        /// <inheritdoc />
        public bool ScrobblingEnabled()
        {
            return _api.Player_GetScrobbleEnabled();
        }

        /// <inheritdoc />
        public bool PlayNext()
        {
            return _api.Player_PlayNextTrack();
        }

        /// <inheritdoc />
        public bool PlayPrevious()
        {
            return _api.Player_PlayPreviousTrack();
        }

        /// <inheritdoc />
        public bool StopPlayback()
        {
            return _api.Player_Stop();
        }

        /// <inheritdoc />
        public bool PlayPause()
        {
            return _api.Player_PlayPause();
        }

        /// <inheritdoc />
        public bool Play()
        {
            return _api.Player_GetPlayState() != PlayState.Playing && _api.Player_PlayPause();
        }

        /// <inheritdoc />
        public bool Pause()
        {
            return _api.Player_GetPlayState() == PlayState.Playing && _api.Player_PlayPause();
        }

        /// <inheritdoc />
        public PlayerStatus GetStatus()
        {
            return new PlayerStatus
            {
                Mute = _api.Player_GetMute(),
                PlayerState = GetState(),
                RepeatMode = GetRepeatMode(),
                Scrobbling = ScrobblingEnabled(),
                Shuffle = GetShuffleState(),
                Volume = GetVolume(),
            };
        }

        /// <inheritdoc />
        public PlayerState GetState()
        {
            switch (_api.Player_GetPlayState())
            {
                case PlayState.Undefined:
                    return PlayerState.Undefined;
                case PlayState.Loading:
                    return PlayerState.Loading;
                case PlayState.Playing:
                    return PlayerState.Playing;
                case PlayState.Paused:
                    return PlayerState.Paused;
                case PlayState.Stopped:
                    return PlayerState.Paused;
                default:
                    throw new Exception(Resources.InvalidPlayState);
            }
        }

        /// <inheritdoc />
        public bool ToggleScrobbling()
        {
            return _api.Player_SetScrobbleEnabled(!_api.Player_GetScrobbleEnabled());
        }

        /// <inheritdoc />
        public int GetVolume()
        {
            return (int)Math.Round(_api.Player_GetVolume() * 100, 1);
        }

        /// <inheritdoc />
        public bool SetVolume(int volume)
        {
            var success = false;
            if (volume >= 0)
            {
                success = _api.Player_SetVolume((float)volume / 100);

                if (_api.Player_GetMute())
                {
                    _api.Player_SetMute(false);
                }
            }

            return success;
        }

        /// <inheritdoc />
        public void ToggleShuffleLegacy()
        {
            _api.Player_SetShuffle(!_api.Player_GetShuffle());
        }

        /// <inheritdoc />
        public bool GetShuffleLegacy()
        {
            return _api.Player_GetShuffle();
        }

        /// <inheritdoc />
        public ShuffleState SwitchShuffle()
        {
            var shuffleEnabled = _api.Player_GetShuffle();
            var autoDjEnabled = _api.Player_GetAutoDjEnabled();

            var shuffleState = GetShuffleState();

            if (shuffleEnabled && !autoDjEnabled)
            {
                var success = _api.Player_StartAutoDj();
                if (success)
                {
                    shuffleState = ShuffleState.Autodj;
                }
            }
            else if (autoDjEnabled)
            {
                _api.Player_EndAutoDj();
            }
            else
            {
                var success = _api.Player_SetShuffle(true);
                if (success)
                {
                    shuffleState = ShuffleState.Shuffle;
                }
            }

            return shuffleState;
        }

        /// <inheritdoc />
        public bool IsMuted()
        {
            return _api.Player_GetMute();
        }

        /// <inheritdoc />
        public bool ToggleMute()
        {
            return _api.Player_SetMute(!_api.Player_GetMute());
        }

        /// <inheritdoc />
        public void ToggleAutoDjLegacy()
        {
            if (!_api.Player_GetAutoDjEnabled())
            {
                _api.Player_StartAutoDj();
            }
            else
            {
                _api.Player_EndAutoDj();
            }
        }

        /// <inheritdoc />
        public bool IsAutoDjEnabledLegacy()
        {
            return _api.Player_GetAutoDjEnabled();
        }
    }
}
