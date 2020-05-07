using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Enumerations
{
    public enum LastfmStatus
    {
        /// <summary>
        /// The last.fm status is neutral.
        /// </summary>
        [EnumMember(Value = "normal")]
        Normal,

        /// <summary>
        /// The last.fm status is loved.
        /// </summary>
        [EnumMember(Value = "Love")]
        Love,

        /// <summary>
        /// The last.fm status is banned.
        /// </summary>
        [EnumMember(Value = "Ban")]
        Ban,
    }
}
