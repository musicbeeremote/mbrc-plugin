using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Entities
{
    [DataContract]
    public class ArtistData
    {
        public ArtistData(string artist, int count)
        {
            Artist = artist;
            Count = count;
        }

        [DataMember(Name = "artist")] public string Artist { get; set; }

        [DataMember(Name = "count")] public int Count { get; set; }
    }
}
