using System;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Enumerations;
using MusicBeeRemote.Core.Model;

namespace MbrcTester.ApiAdapters
{
    /// <inheritdoc />
    public class MockPlayerApiAdapter : IPlayerApiAdapter
    {
        private readonly MockPlayer _mockPlayer;
        private readonly MockPlayerState _mockPlayerState;

        public MockPlayerApiAdapter(MockPlayerState mockPlayerState, MockPlayer mockPlayer)
        {
            _mockPlayerState = mockPlayerState;
            _mockPlayer = mockPlayer;
        }

        /// <inheritdoc />
        public ShuffleState GetShuffleState()
        {
            var shuffleEnabled = _mockPlayerState.GetShuffle();
            var autoDjEnabled = _mockPlayerState.GetAutoDjEnabled();
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
            return _mockPlayerState.GetRepeat();
        }

        /// <inheritdoc />
        public bool ToggleRepeatMode()
        {
            switch (_mockPlayerState.GetRepeat())
            {
                case Repeat.None:
                    return _mockPlayerState.SetRepeat(Repeat.All);

                case Repeat.All:
                    return _mockPlayerState.SetRepeat(Repeat.None);

                case Repeat.One:
                    return _mockPlayerState.SetRepeat(Repeat.None);
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public bool ScrobblingEnabled()
        {
            return _mockPlayerState.GetScrobbleEnabled();
        }

        /// <inheritdoc />
        public bool PlayNext()
        {
            return _mockPlayer.PlayNextTrack();
        }

        /// <inheritdoc />
        public bool PlayPrevious()
        {
            return _mockPlayer.PlayPreviousTrack();
        }

        /// <inheritdoc />
        public bool StopPlayback()
        {
            return _mockPlayer.Stop();
        }

        /// <inheritdoc />
        public bool PlayPause()
        {
            return _mockPlayer.PlayPause();
        }

        /// <inheritdoc />
        public bool Play()
        {
            return _mockPlayerState.GetPlayerState() != PlayerState.Playing && _mockPlayer.PlayPause();
        }

        /// <inheritdoc />
        public bool Pause()
        {
            return _mockPlayerState.GetPlayerState() == PlayerState.Playing && _mockPlayer.PlayPause();
        }

        /// <inheritdoc />
        public PlayerStatus GetStatus()
        {
            return new PlayerStatus
            {
                Mute = _mockPlayerState.GetMute(),
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
            return _mockPlayerState.GetPlayerState();
        }

        /// <inheritdoc />
        public bool ToggleScrobbling()
        {
            return _mockPlayerState.SetScrobbleEnabled(!_mockPlayerState.GetScrobbleEnabled());
        }

        /// <inheritdoc />
        public int GetVolume()
        {
            return (int)Math.Round(_mockPlayerState.GetVolume() * 100, 1);
        }

        /// <inheritdoc />
        public bool SetVolume(int volume)
        {
            if (volume < 0)
            {
                return false;
            }

            var success = _mockPlayerState.SetVolume((float)volume / 100);

            if (_mockPlayerState.GetMute())
            {
                _mockPlayerState.SetMute(false);
            }

            return success;
        }

        /// <inheritdoc />
        public void ToggleShuffleLegacy()
        {
            _mockPlayerState.SetShuffle(!_mockPlayerState.GetShuffle());
        }

        /// <inheritdoc />
        public bool GetShuffleLegacy()
        {
            return _mockPlayerState.GetShuffle();
        }

        /// <inheritdoc />
        public ShuffleState SwitchShuffle()
        {
            var shuffleEnabled = _mockPlayerState.GetShuffle();
            var autoDjEnabled = _mockPlayerState.GetAutoDjEnabled();

            var shuffleState = GetShuffleState();

            if (shuffleEnabled && !autoDjEnabled)
            {
                var success = _mockPlayer.StartAutoDj();
                if (success)
                {
                    shuffleState = ShuffleState.Autodj;
                }
            }
            else if (autoDjEnabled)
            {
                _mockPlayer.EndAutoDj();
            }
            else
            {
                var success = _mockPlayerState.SetShuffle(true);
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
            return _mockPlayerState.GetMute();
        }

        /// <inheritdoc />
        public bool ToggleMute()
        {
            return _mockPlayerState.ToggleMute();
        }

        /// <inheritdoc />
        public void ToggleAutoDjLegacy()
        {
            if (!_mockPlayerState.GetAutoDjEnabled())
            {
                _mockPlayer.StartAutoDj();
            }
            else
            {
                _mockPlayer.EndAutoDj();
            }
        }

        /// <inheritdoc />
        public bool IsAutoDjEnabledLegacy()
        {
            return _mockPlayerState.GetAutoDjEnabled();
        }
    }
}
