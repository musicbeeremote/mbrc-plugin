using MusicBeeRemote.Core.Enumerations;

namespace MbrcTester
{
    public class MockPlayerState
    {
        private bool shuffle;
        private bool mute;
        private Repeat repeat;
        private bool scrobble;
        private float volume;
        private PlayerState playerState;
        private bool autoDJ;

        public MockPlayerState()
        {
            repeat = Repeat.None;
            volume = 0;
            playerState = PlayerState.Undefined;
        }

        public bool GetShuffle()
        {
            return shuffle;
        }

        public bool SetShuffle(bool shuffle)
        {
            this.shuffle = shuffle;
            return true;
        }

        public bool GetAutoDjEnabled()
        {
            return autoDJ;
        }

        public bool ToggleMute()
        {
            mute = !mute;
            return true;
        }

        public Repeat GetRepeat()
        {
            return repeat;
        }

        public bool SetRepeat(Repeat repeat)
        {
            this.repeat = repeat;
            return true;
        }

        public bool GetScrobbleEnabled()
        {
            return scrobble;
        }

        public PlayerState GetPlayerState()
        {
            return playerState;
        }

        public void SetPlayerState(PlayerState playerState)
        {
            this.playerState = playerState;
        }

        public float GetVolume()
        {
            return volume;
        }

        public bool SetVolume(float volume)
        {
            this.volume = volume;
            return true;
        }

        public bool GetMute()
        {
            return mute;
        }

        public void SetMute(bool mute)
        {
            this.mute = mute;
        }

        public bool SetScrobbleEnabled(bool enabled)
        {
            scrobble = enabled;
            return true;
        }

        public void SetAutoDj(bool enabled)
        {
            autoDJ = enabled;
        }
    }
}