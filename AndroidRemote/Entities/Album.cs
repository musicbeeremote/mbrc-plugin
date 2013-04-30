using System;
using System.Xml.Linq;

namespace MusicBeePlugin.AndroidRemote.Entities
{
    class Album : IEquatable<Album>
    {
        public Album(string artist, string album)
        {
            this.album = album;
            this.artist = artist;
            TrackCount = 1;
        }

        public string album { get; private set; }

        public string artist { get; private set; }

        public void IncreaseCount()
        {
            TrackCount++;
        }

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
