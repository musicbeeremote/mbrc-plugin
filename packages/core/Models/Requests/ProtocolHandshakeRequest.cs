using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Requests
{
    /// <summary>
    ///     Request payload for protocol handshake
    /// </summary>
    [DataContract]
    public class ProtocolHandshakeRequest
    {
        [DataMember(Name = "protocol_version")] public int ProtocolVersion { get; set; }

        [DataMember(Name = "no_broadcast")] public bool NoBroadcast { get; set; }

        [DataMember(Name = "client_id")] public string ClientId { get; set; }
    }
}
