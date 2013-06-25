using System;

namespace MusicBeePlugin.AndroidRemote.Entities
{
    class Track :IEquatable<Track>, IComparable<Track>
    {
        public Track(string artist, string title, string src)
        {
            this.title = title;
            this.artist = artist;
            this.trackno = 0;
            this.src = src;
        }

        public Track(string artist, string title, int trackNo, string src)
        {
            this.artist = artist;
            this.title = title;
            this.src = src;
            this.trackno = trackNo;
        }

        public string src { get; private set; }

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
