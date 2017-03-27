
namespace MusicBeeRemoteCore.Remote.Model.Entities
{
    [DataContract]
    internal class Genre
    {
        [DataMember(Name = "genre")]
        public string Name { get; set; }

        [DataMember(Name = "count")]
        public int Count { get; set; }

        public Genre(string name, int count)
        {
            Name = name;
            Count = count;
        }
    }
}
