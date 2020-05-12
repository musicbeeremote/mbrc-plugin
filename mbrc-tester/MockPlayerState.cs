using MusicBeeRemote.Core.Enumerations;

namespace MbrcTester
{
    public class MockPlayerState
    {
        private bool _shuffle;
        private bool _mute;
        private Repeat _repeat;
        private bool _scrobble;
        private float _volume;
        private PlayerState _playerState;
        private bool _autoDj;

        public MockPlayerState()
        {
            _repeat = Repeat.None;
            _volume = 0;
            _playerState = PlayerState.Undefined;
        }

        public bool GetShuffle()
        {
            return _shuffle;
        }

        public bool SetShuffle(bool shuffle)
        {
            this._shuffle = shuffle;
            return true;
        }

        public bool GetAutoDjEnabled()
        {
            return _autoDj;
        }

        public bool ToggleMute()
        {
            _mute = !_mute;
            return true;
        }

        public Repeat GetRepeat()
        {
            return _repeat;
        }

        public bool SetRepeat(Repeat repeat)
        {
            this._repeat = repeat;
            return true;
        }

        public bool GetScrobbleEnabled()
        {
            return _scrobble;
        }

        public PlayerState GetPlayerState()
        {
            return _playerState;
        }

        public void SetPlayerState(PlayerState playerState)
        {
            this._playerState = playerState;
        }

        public float GetVolume()
        {
            return _volume;
        }

        public bool SetVolume(float volume)
        {
            this._volume = volume;
            return true;
        }

        public bool GetMute()
        {
            return _mute;
        }

        public void SetMute(bool mute)
        {
            this._mute = mute;
        }

        public bool SetScrobbleEnabled(bool enabled)
        {
            _scrobble = enabled;
            return true;
        }

        public void SetAutoDj(bool enabled)
        {
            _autoDj = enabled;
        }
    }
}
