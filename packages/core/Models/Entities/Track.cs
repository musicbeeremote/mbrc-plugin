using System;
using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Entities
{
    [DataContract]
    public class Track : IEquatable<Track>, IComparable<Track>
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
            return other != null && other.Artist.Equals(Artist, StringComparison.Ordinal) && other.Title.Equals(Title, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Track);
        }

        public override int GetHashCode()
        {
            return (Artist?.GetHashCode() ?? 0) ^ (Title?.GetHashCode() ?? 0);
        }

        // Comparison operators required for IComparable
        public static bool operator ==(Track left, Track right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is null)
                return false;
            return left.Equals(right);
        }

        public static bool operator !=(Track left, Track right)
        {
            return !(left == right);
        }

        public static bool operator <(Track left, Track right)
        {
            return left is null ? right != null : left.CompareTo(right) < 0;
        }

        public static bool operator <=(Track left, Track right)
        {
            return left is null || left.CompareTo(right) <= 0;
        }

        public static bool operator >(Track left, Track right)
        {
            return left != null && left.CompareTo(right) > 0;
        }

        public static bool operator >=(Track left, Track right)
        {
            return left is null ? right is null : left.CompareTo(right) >= 0;
        }
    }
}
