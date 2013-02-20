using System;
using System.Xml.Linq;

namespace MusicBeePlugin.AndroidRemote.Entities
{
    class Album : IEquatable<Album>
    {
        private readonly string album;
        private readonly string artist;
        private int count;

        public Album(string artist, string album)
        {
            this.album = album;
            this.artist = artist;
            count = 1;
        }

        public string AlbumName
        {
            get { return album; }
        }

        public string AlbumArtist
        {
            get { return artist; }
        }

        public void IncreaseCount()
        {
            count++;
        }

        public int TrackCount
        {
            get { return count; }
        }

        public XElement toXElement()
        {
            return new XElement("album", 
                new XElement("albumartist", artist),
                new XElement("albumname", album),
                new XElement("count", count));
        }

        public bool Equals(Album other)
        {
            return other.AlbumArtist.Equals(artist) && other.AlbumName.Equals(album);
        }
    }
}
