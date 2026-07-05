using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Entities
{
    [DataContract]
    public class RadioStation
    {
        [DataMember(Name = "name")] public string Name { get; set; }

        [DataMember(Name = "url")] public string Url { get; set; }
    }
}
