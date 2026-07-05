using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Commands
{
    /// <summary>
    /// Response for track removal operations
    /// </summary>
    [DataContract]
    public class RemoveTrackResponse
    {
        /// <summary>
        /// Indicates whether the removal was successful
        /// </summary>
        [DataMember(Name = "success")]
        public bool Success { get; set; }

        /// <summary>
        /// The index of the removed track
        /// </summary>
        [DataMember(Name = "index")]
        public int Index { get; set; }

        public RemoveTrackResponse()
        {
        }

        public RemoveTrackResponse(bool success, int index)
        {
            Success = success;
            Index = index;
        }
    }
}
