using System;
using System.Runtime.Serialization;
using System.Xml.Linq;

namespace MusicBeePlugin.AndroidRemote.Entities
{
    [DataContract]
    class Album : IEquatable<Album>
    {
        
        public Album(string artist, string album)
        {
            this.album = album;
            this.artist = artist;
            TrackCount = 1;
        }

        [DataMember(Name = "album")]
        public string album { get; }

        [DataMember(Name = "artist")]
        public string artist { get; }

        public void IncreaseCount()
        {
            TrackCount++;
        }

        [DataMember(Name = "count")]
        public int TrackCount { get; private set; }

        public bool Equals(Album other)
        {
            return other.artist.Equals(artist) && other.album.Equals(album);
        }

        public override int GetHashCode()
        {
            return artist.GetHashCode() ^ album.GetHashCode();
        }
    }
}
