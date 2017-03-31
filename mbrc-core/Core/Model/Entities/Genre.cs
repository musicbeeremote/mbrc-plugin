
using System.Runtime.Serialization;

namespace MusicBeeRemote.Core.Model.Entities
{
    [DataContract]
    public class Genre
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
