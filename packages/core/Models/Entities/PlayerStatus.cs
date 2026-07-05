using System.Runtime.Serialization;
using MusicBeePlugin.Protocol.Messages;

namespace MusicBeePlugin.Models.Entities
{
    /// <summary>
    ///     Represents the current player status including all player state information
    /// </summary>
    [DataContract]
    public class PlayerStatus
    {
        [DataMember(Name = ProtocolConstants.PlayerRepeat)]
        public string Repeat { get; set; }

        [DataMember(Name = ProtocolConstants.PlayerMute)]
        public bool Mute { get; set; }

        [DataMember(Name = ProtocolConstants.PlayerShuffle)]
        public object Shuffle { get; set; } // Can be bool or ShuffleState depending on protocol

        [DataMember(Name = ProtocolConstants.PlayerScrobble)]
        public bool Scrobble { get; set; }

        [DataMember(Name = ProtocolConstants.PlayerState)]
        public string State { get; set; }

        [DataMember(Name = ProtocolConstants.PlayerVolume)]
        public string Volume { get; set; }
    }
}
