using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Entities
{
    [DataContract]
    public class NowPlayingListTrack
    {
        public NowPlayingListTrack(string artist, string title, int position)
        {
            Position = position;
            Artist = artist;
            Title = title;
        }

        public NowPlayingListTrack()
        {
        }

        [DataMember(Name = "artist")] public string Artist { get; set; }

        [DataMember(Name = "title")] public string Title { get; set; }

        [DataMember(Name = "path")] public string Path { get; set; }

        [DataMember(Name = "position")] public int Position { get; set; }
    }
}
