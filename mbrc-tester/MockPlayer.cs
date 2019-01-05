namespace MbrcTester
{
    public class MockPlayer
    {
        public MockTrackMetadata playingTrack { get; set; } = new MockTrackMetadata();

        public bool PlayNextTrack()
        {
            throw new System.NotImplementedException();
        }

        public bool PlayPreviousTrack()
        {
            throw new System.NotImplementedException();
        }

        public bool Stop()
        {
            throw new System.NotImplementedException();
        }

        public bool PlayPause()
        {
            throw new System.NotImplementedException();
        }

        public bool StartAutoDj()
        {
            throw new System.NotImplementedException();
        }

        public void EndAutoDj()
        {
            throw new System.NotImplementedException();
        }
    }
}