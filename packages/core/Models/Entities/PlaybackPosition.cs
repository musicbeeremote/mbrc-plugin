using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Entities
{
    /// <summary>
    ///     Represents the current playback position and total duration
    /// </summary>
    [DataContract]
    public class PlaybackPosition
    {
        public PlaybackPosition()
        {
            Current = 0;
            Total = 0;
        }

        public PlaybackPosition(int current, int total)
        {
            Current = current;
            Total = total;
        }

        /// <summary>
        ///     Current playback position in milliseconds
        /// </summary>
        [DataMember(Name = "current")]
        public int Current { get; set; }

        /// <summary>
        ///     Total track duration in milliseconds
        /// </summary>
        [DataMember(Name = "total")]
        public int Total { get; set; }
    }
}
