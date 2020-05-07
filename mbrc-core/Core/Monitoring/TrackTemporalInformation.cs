using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Monitoring
{
    [DataContract]
    public class TrackTemporalInformation
    {
        public TrackTemporalInformation(int position, int duration)
        {
            Position = position;
            Duration = duration;
        }

        [DataMember(Name = "current")]
        public int Position { get; }

        [DataMember(Name = "total")]
        public int Duration { get; }
    }
}
