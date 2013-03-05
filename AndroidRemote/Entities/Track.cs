using System;
using System.Xml.Linq;

namespace MusicBeePlugin.AndroidRemote.Entities
{
    class Track :IEquatable<Track>, IComparable<Track>
    {
        private readonly string title;
        private readonly string artist;
        private readonly int trackNo;

        public Track(string artist, string title)
        {
            this.title = title;
            this.artist = artist;
            this.trackNo = 0;
        }

        public Track(string artist, string title, int trackNo)
        {
            this.artist = artist;
            this.title = title;
            this.trackNo = trackNo;
        }

        public string Artist
        {
            get { return artist; }
        }

        public string Title
        {
            get { return title; }
        }

        public int TrackNo
        {
            get { return trackNo; }
        }

        public bool Equals(Track other)
        {
            return (other.Artist.Equals(artist) && other.Title.Equals(title));
        }

        public int CompareTo(Track other)
        {
            return other == null ? 1 : trackNo.CompareTo(other.TrackNo);
        }
    }
}
