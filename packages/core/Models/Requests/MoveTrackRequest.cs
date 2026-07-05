using System.Runtime.Serialization;
using MusicBeePlugin.Commands.Contracts;

namespace MusicBeePlugin.Models.Requests
{
    /// <summary>
    ///     Request payload for moving a track in the now playing list
    /// </summary>
    [DataContract]
    public class MoveTrackRequest : IValidatable
    {
        [DataMember(Name = "from")] public int? From { get; set; }

        [DataMember(Name = "to")] public int? To { get; set; }

        /// <summary>
        ///     Returns true if both from and to positions are provided
        /// </summary>
        public bool IsValid => From.HasValue && To.HasValue;
    }
}
