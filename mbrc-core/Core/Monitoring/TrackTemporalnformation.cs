using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Monitoring
{
    [DataContract]
    public class TrackTemporalnformation
    {
        [DataMember(Name = "current")]
        public int Position { get; }

        [DataMember(Name = "total")]
        public int Duration { get; }

        public TrackTemporalnformation(int position, int duration)
        {
            Position = position;
            Duration = duration;
        }
    }
}