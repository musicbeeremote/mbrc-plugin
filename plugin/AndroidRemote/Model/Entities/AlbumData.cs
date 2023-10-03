using System;
using System.Runtime.Serialization;

namespace MusicBeePlugin.AndroidRemote.Model.Entities
{
    [DataContract]
    internal class AlbumData : IEquatable<AlbumData>
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
            return other != null && other.Artist.Equals(Artist) && other.Album.Equals(Album);
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