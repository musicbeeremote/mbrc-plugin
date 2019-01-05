using System;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Enumerations;
using MusicBeeRemote.Core.Model;

namespace MbrcTester.ApiAdapters
{
    public class MockPlayerApiAdapter : IPlayerApiAdapter
    {
        private readonly MockPlayerState _mockPlayerState;
        private readonly MockPlayer _mockPlayer;

        public MockPlayerApiAdapter(MockPlayerState mockPlayerState, MockPlayer mockPlayer)
        {
            _mockPlayerState = mockPlayerState;
            _mockPlayer = mockPlayer;
        }

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

        public Repeat GetRepeatMode()
        {
            return _mockPlayerState.GetRepeat();
        }

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
                    throw new ArgumentOutOfRangeException();
            }
        }


        public bool ScrobblingEnabled()
        {
            return _mockPlayerState.GetScrobbleEnabled();
        }

        public bool PlayNext()
        {
            return _mockPlayer.PlayNextTrack();
        }

        public bool PlayPrevious()
        {
            return _mockPlayer.PlayPreviousTrack();
        }

        public bool StopPlayback()
        {
            return _mockPlayer.Stop();
        }

        public bool PlayPause()
        {
            return _mockPlayer.PlayPause();
        }

        public bool Play()
        {
            return _mockPlayerState.GetPlayerState() != PlayerState.Playing && _mockPlayer.PlayPause();
        }

        public bool Pause()
        {
            return _mockPlayerState.GetPlayerState() == PlayerState.Playing && _mockPlayer.PlayPause();
        }

        public PlayerStatus GetStatus()
        {
            return new PlayerStatus
            {
                Mute = _mockPlayerState.GetMute(),
                PlayerState = GetState(),
                RepeatMode = GetRepeatMode(),
                Scrobbling = ScrobblingEnabled(),
                Shuffle = GetShuffleState(),
                Volume = GetVolume()
            };
        }

        public PlayerState GetState()
        {
            return _mockPlayerState.GetPlayerState();
        }

        public bool ToggleScrobbling()
        {
            return _mockPlayerState.SetScrobbleEnabled(!_mockPlayerState.GetScrobbleEnabled());
        }

        public int GetVolume()
        {
            return (int) Math.Round(_mockPlayerState.GetVolume() * 100, 1);
        }

        public bool SetVolume(int volume)
        {
            if (volume < 0) return false;
            
            var success = _mockPlayerState.SetVolume((float) volume / 100);

            if (_mockPlayerState.GetMute())
            {
                _mockPlayerState.SetMute(false);
            }

            return success;
        }

        public void ToggleShuffleLegacy()
        {
            _mockPlayerState.SetShuffle(!_mockPlayerState.GetShuffle());
        }

        public bool GetShuffleLegacy()
        {
            return _mockPlayerState.GetShuffle();
        }

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

        public bool IsMuted()
        {
            return _mockPlayerState.GetMute();
        }

        public bool ToggleMute()
        {
            return _mockPlayerState.ToggleMute();
        }

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

        public bool IsAutoDjEnabledLegacy()
        {
            return _mockPlayerState.GetAutoDjEnabled();
        }
    }
}