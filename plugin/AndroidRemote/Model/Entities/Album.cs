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
        public string album { get; private set; }

        [DataMember(Name = "artist")]
        public string artist { get; private set; }

        public void IncreaseCount()
        {
            TrackCount++;
        }

        [DataMember(Name = "count")]
        public int TrackCount { get; private set; }

        public XElement toXElement()
        {
            return new XElement("album", 
                new XElement("albumartist", artist),
                new XElement("albumname", album),
                new XElement("count", TrackCount));
        }

        public bool Equals(Album other)
        {
            return other.artist.Equals(artist) && other.album.Equals(album);
        }
    }
}
