using System.Runtime.Serialization;

namespace MusicBeePlugin.AndroidRemote.Commands
{
    [DataContract]
    public class QueueResponse
    {
        [DataMember(Name = "code")] public int Code { get; set; }
    }
}