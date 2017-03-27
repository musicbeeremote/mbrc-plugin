namespace MusicBeeRemoteCore.Remote.Commands
{
    [DataContract]
    public class QueueResponse
    {
        [DataMember(Name = "code")]
        public int Code { get; set; }
    }
}