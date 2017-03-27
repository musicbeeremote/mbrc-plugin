namespace MusicBeeRemoteCore.Remote.Entities
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