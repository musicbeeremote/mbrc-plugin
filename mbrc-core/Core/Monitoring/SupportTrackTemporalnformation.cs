using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Monitoring
{
    /// <summary>
    /// Temporal Information about the playing track. This Object contains information about the
    /// playback <see cref="Position"/> and <see cref="Duration"/> of the currently playing 
    /// track. This is the support version of the object that was used up to v4 of the Json socket api.
    /// The serialization of the temporal data used capital letters.    
    /// </summary>
    [DataContract]
    public class SupportTrackTemporalnformation
    {
        [DataMember(Name = "Position")]
        public int Position { get; }

        [DataMember(Name = "Duration")]
        public int Duration { get; }

        public SupportTrackTemporalnformation(int position, int duration)
        {
            Position = position;
            Duration = duration;
        }
    }
}