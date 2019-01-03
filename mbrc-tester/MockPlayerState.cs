using MusicBeeRemote.Core.Enumerations;

namespace MbrcTester
{
    public class MockPlayerState
    {
        private bool shuffle;
        private bool mute;

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
            throw new System.NotImplementedException();
        }

        public bool ToggleMute()
        {
            throw new System.NotImplementedException();
        }

        public Repeat GetRepeat()
        {
            throw new System.NotImplementedException();
        }

        public bool SetRepeat(Repeat repeat)
        {
            throw new System.NotImplementedException();
        }

        public bool GetScrobbleEnabled()
        {
            throw new System.NotImplementedException();
        }

        public PlayerState GetPlayerState()
        {
            throw new System.NotImplementedException();
        }

        public float GetVolume()
        {
            throw new System.NotImplementedException();
        }

        public bool SetVolume(float volume)
        {
            throw new System.NotImplementedException();
        }

        public bool GetMute()
        {
            throw new System.NotImplementedException();
        }

        public void SetMute(bool mute)
        {
            throw new System.NotImplementedException();
        }

        public bool SetScrobbleEnabled(bool enabled)
        {
            throw new System.NotImplementedException();
        }
    }
}