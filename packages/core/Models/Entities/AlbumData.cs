using System;
using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Entities
{
    [DataContract]
    public class AlbumData : IEquatable<AlbumData>
    {
        public AlbumData(string artist, string album)
        {
            Album = album;
            Artist = artist;
            TrackCount = 1;
        }

        [DataMember(Name = "album")] public string Album { get; }

        [DataMember(Name = "artist")] public string Artist { get; }

        [DataMember(Name = "count")] public int TrackCount { get; private set; }

        public bool Equals(AlbumData other)
        {
            return other != null && other.Artist.Equals(Artist, StringComparison.Ordinal) && other.Album.Equals(Album, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as AlbumData);
        }

        public void IncreaseCount()
        {
            TrackCount++;
        }

        public override int GetHashCode()
        {
            return Artist.GetHashCode() ^ Album.GetHashCode();
        }
    }
}
