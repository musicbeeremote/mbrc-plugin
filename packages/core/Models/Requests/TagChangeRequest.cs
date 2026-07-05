using System.Runtime.Serialization;
using MusicBeePlugin.Commands.Contracts;

namespace MusicBeePlugin.Models.Requests
{
    /// <summary>
    ///     Request payload for changing a track tag
    /// </summary>
    [DataContract]
    public class TagChangeRequest : IValidatable
    {
        [DataMember(Name = "tag")] public string Tag { get; set; }

        [DataMember(Name = "value")] public string Value { get; set; }

        /// <summary>
        ///     Returns true if the tag name is provided
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Tag);
    }
}
