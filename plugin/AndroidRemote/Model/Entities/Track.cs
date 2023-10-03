using System;
using System.Runtime.Serialization;

namespace MusicBeePlugin.AndroidRemote.Model.Entities
{
    [DataContract]
    internal class Track : IEquatable<Track>, IComparable<Track>
    {
        public Track()
        {
        }

        public Track(string artist, string title, int trackNo, string src)
        {
            Artist = artist;
            Title = title;
            Src = src;
            TrackNo = trackNo;
        }

        [DataMember(Name = "src")] public string Src { get; set; }

        [DataMember(Name = "artist")] public string Artist { get; set; }

        [DataMember(Name = "title")] public string Title { get; set; }

        [DataMember(Name = "trackno")] public int TrackNo { get; set; }

        [DataMember(Name = "disc")] public int Disc { get; set; }

        [DataMember(Name = "album")] public string Album { get; set; }

        [DataMember(Name = "album_artist")] public string AlbumArtist { get; set; }

        [DataMember(Name = "genre")] public string Genre { get; set; }

        public int CompareTo(Track other)
        {
            return other == null ? 1 : TrackNo.CompareTo(other.TrackNo);
        }

        public bool Equals(Track other)
        {
            return other != null && other.Artist.Equals(Artist) && other.Title.Equals(Title);
        }
    }
}