using System.Runtime.Serialization;

namespace MusicBeeRemoteCore.Monitoring
{
    [DataContract]
    public class TrackTemporalnformation
    {
        [DataMember(Name = "position")]
        public int Position { get; }

        [DataMember(Name = "duration")]
        public int Duration { get; }

        public TrackTemporalnformation(int position, int duration)
        {
            Position = position;
            Duration = duration;
        }
    }
}