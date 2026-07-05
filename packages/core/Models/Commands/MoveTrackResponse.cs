using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Commands
{
    /// <summary>
    /// Response for track move operations
    /// </summary>
    [DataContract]
    public class MoveTrackResponse
    {
        /// <summary>
        /// Indicates whether the move was successful
        /// </summary>
        [DataMember(Name = "success")]
        public bool Success { get; set; }

        /// <summary>
        /// The original position of the track
        /// </summary>
        [DataMember(Name = "from")]
        public int From { get; set; }

        /// <summary>
        /// The new position of the track
        /// </summary>
        [DataMember(Name = "to")]
        public int To { get; set; }

        public MoveTrackResponse()
        {
        }

        public MoveTrackResponse(bool success, int from, int to)
        {
            Success = success;
            From = from;
            To = to;
        }
    }
}
