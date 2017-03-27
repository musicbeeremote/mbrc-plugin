namespace MusicBeeRemoteCore.Monitoring
{
    //todo make serializable
    public class TrackTemporalnformation
    {
        public int Position { get; }
        public int Duration { get; }

        public TrackTemporalnformation(int position, int duration)
        {
            Position = position;
            Duration = duration;
        }
    }
}