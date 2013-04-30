using System;
using System.Xml.Linq;

namespace MusicBeePlugin.AndroidRemote.Entities
{
    class Track :IEquatable<Track>, IComparable<Track>
    {
        public Track(string artist, string title)
        {
            this.title = title;
            this.artist = artist;
            this.trackno = 0;
        }

        public Track(string artist, string title, int trackNo)
        {
            this.artist = artist;
            this.title = title;
            this.trackno = trackNo;
        }

        public string artist { get; private set; }

        public string title { get; private set; }

        public int trackno { get; private set; }

        public bool Equals(Track other)
        {
            return (other.artist.Equals(artist) && other.title.Equals(title));
        }

        public int CompareTo(Track other)
        {
            return other == null ? 1 : trackno.CompareTo(other.trackno);
        }
    }
}
