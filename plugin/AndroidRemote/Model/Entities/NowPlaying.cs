using System.Runtime.Serialization;

namespace MusicBeePlugin.AndroidRemote.Model.Entities
{
    [DataContract]
    public class NowPlaying
    {
        [DataMember(Name = "artist")] public string Artist { get; set; }

        [DataMember(Name = "album")] public string Album { get; set; }

        [DataMember(Name = "album_artist")] public string AlbumArtist { get; set; }

        [DataMember(Name = "title")] public string Title { get; set; }

        [DataMember(Name = "path")] public string Path { get; set; }

        [DataMember(Name = "position")] public int Position { get; set; }
    }
}