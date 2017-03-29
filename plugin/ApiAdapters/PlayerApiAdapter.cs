using System;
using System.Windows.Forms;
using MusicBeeRemoteCore.Core.ApiAdapters;
using MusicBeeRemoteCore.Core.Model;
using MusicBeeRemoteCore.Remote.Enumerations;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.ApiAdapters
{
    public class PlayerApiAdapter : IPlayerApiAdapter
    {
        private readonly MusicBeeApiInterface _api;

        public PlayerApiAdapter(MusicBeeApiInterface api)
        {
            _api = api;
        }

        public ShuffleState GetShuffleState()
        {
            var shuffleEnabled = _api.Player_GetShuffle();
            var autoDjEnabled = _api.Player_GetAutoDjEnabled();
            var state = ShuffleState.off;
            if (shuffleEnabled && !autoDjEnabled)
            {
                state = ShuffleState.shuffle;
            }
            else if (autoDjEnabled)
            {
                state = ShuffleState.autodj;
            }

            return state;
        }

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
                    throw new ArgumentOutOfRangeException();
            }
            return repeatMode;
        }

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
                    throw new ArgumentOutOfRangeException();
            }
        }


        public bool ScrobblingEnabled()
        {
            return _api.Player_GetScrobbleEnabled();
        }

        public bool PlayNext()
        {
            return _api.Player_PlayNextTrack();
        }

        public bool PlayPrevious()
        {
            return _api.Player_PlayPreviousTrack();
        }

        public bool StopPlayback()
        {
            return _api.Player_Stop();
        }

        public bool PlayPause()
        {
            return _api.Player_PlayPause();
        }

        public bool Play()
        {
            return _api.Player_GetPlayState() != PlayState.Playing && _api.Player_PlayPause();
        }

        public bool Pause()
        {
            return _api.Player_GetPlayState() == PlayState.Playing && _api.Player_PlayPause();
        }

        public PlayerStatus GetStatus()
        {
            return new PlayerStatus
            {
                Mute = _api.Player_GetMute(),
                PlayerState = GetState(),
                RepeatMode = GetRepeatMode(),
                Scrobbling = ScrobblingEnabled(),
                Shuffle = GetShuffleState(),
                Volume = GetVolume()
            };
        }

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
                    throw new ArgumentOutOfRangeException();
            }
        }

        public bool ToggleScrobbling()
        {
            return _api.Player_SetScrobbleEnabled(!_api.Player_GetScrobbleEnabled());
        }

        public int GetVolume()
        {
            return (int) Math.Round(_api.Player_GetVolume() * 100, 1);
        }

        public bool SetVolume(int volume)
        {
            var success = false;
            if (volume >= 0)
            {
                success = _api.Player_SetVolume((float) volume / 100);

                if (_api.Player_GetMute())
                {
                    _api.Player_SetMute(false);
                }
            }

            return success;
        }

        public void ToggleShuffleLegacy()
        {
            _api.Player_SetShuffle(!_api.Player_GetShuffle());
        }

        public bool GetShuffleLegacy()
        {
            return _api.Player_GetShuffle();
        }

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
                    shuffleState = ShuffleState.autodj;
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
                    shuffleState = ShuffleState.shuffle;
                }
            }

            return shuffleState;
        }

        public bool IsMuted()
        {
            return _api.Player_GetMute();
        }

        public bool ToggleMute()
        {
            return _api.Player_SetMute(!_api.Player_GetMute());
        }

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

        public bool IsAutoDjEnabledLegacy()
        {
            return _api.Player_GetAutoDjEnabled();
        }
    }
}