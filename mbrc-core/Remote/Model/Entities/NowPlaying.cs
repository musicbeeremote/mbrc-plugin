using System.Runtime.Serialization;

namespace MusicBeeRemoteCore.Remote.Model.Entities
{
    [DataContract]
    public class NowPlaying
    {
        [DataMember(Name = "artist")]
        public string Artist { get; set; }

        [DataMember(Name = "title")]
        public string Title { get; set; }

        [DataMember(Name = "path")]
        public string Path { get; set; }

        [DataMember(Name = "position")]
        public int Position { get; set; }
    }
}