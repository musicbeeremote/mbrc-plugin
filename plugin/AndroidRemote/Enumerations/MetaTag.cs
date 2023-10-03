using System.Runtime.Serialization;

namespace MusicBeePlugin.AndroidRemote.Enumerations
{
    /// <summary>
    ///     Represents the tag of the search filter.
    /// </summary>
    public enum MetaTag
    {
        [EnumMember(Value = "artist")]
        Artist,
        [EnumMember(Value = "album")]
        Album,
        [EnumMember(Value = "genre")]
        Genre,
        [EnumMember(Value = "title")]
        Title
    }
}