namespace MusicBeeRemoteCore.Remote.Model.Entities
{
    [DataContract]
    internal class Artist
    {
        [DataMember(Name = "artist")]
        public string Name { get; set; }

        [DataMember(Name = "count")]
        public int Count { get; set; }

        public Artist(string name, int count)
        {
            Name = name;
            Count = count;
        }
    }
}
