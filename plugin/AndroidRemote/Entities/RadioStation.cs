using System.Runtime.Serialization;

namespace MusicBeePlugin.AndroidRemote.Entities
{
    [DataContract]
    public class RadioStation
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "url")]
        public string Url { get; set; }
    }
}