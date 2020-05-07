using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Enumerations
{
    /// <summary>
    /// Represents the repeat mode of the server.
    /// </summary>
    public enum Repeat
    {
        /// <summary>
        /// Represents the deactivated repeat functionality
        /// </summary>
        [EnumMember(Value = "none")]
        None,

        /// <summary>
        /// Represents the on track repeat
        /// </summary>
        [EnumMember(Value = "one")]
        One,

        /// <summary>
        /// Represents the repeat of the whole playlist
        /// </summary>
        [EnumMember(Value = "all")]
        All,
    }
}
