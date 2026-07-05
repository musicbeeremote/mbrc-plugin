using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Entities
{
    [DataContract]
    public class GenreData
    {
        public GenreData(string genre, int count)
        {
            Genre = genre;
            Count = count;
        }

        [DataMember(Name = "genre")] public string Genre { get; set; }

        [DataMember(Name = "count")] public int Count { get; set; }
    }
}
