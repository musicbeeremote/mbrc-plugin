using System.Runtime.Serialization;

namespace MusicBeePlugin.Enumerations
{
    /// <summary>
    ///     Represents the tag of the search filter.
    ///     StringEnumConverter is applied globally via SocketMessage.SerializerSettings.
    /// </summary>
    public enum MetaTag
    {
        [EnumMember(Value = "artist")] Artist,
        [EnumMember(Value = "album")] Album,
        [EnumMember(Value = "genre")] Genre,
        [EnumMember(Value = "title")] Title
    }
}
