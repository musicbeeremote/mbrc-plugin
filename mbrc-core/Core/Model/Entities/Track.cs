using System;
using System.Runtime.Serialization;
using LiteDB;

namespace MusicBeeRemote.Core.Model.Entities
{
    [DataContract]
    public sealed class Track : IEquatable<Track>, IComparable<Track>
    {
        [BsonId]
        [DataMember(Name = "src")]
        public string Src { get; set; }

        [DataMember(Name = "artist")]
        public string Artist { get; set; }

        [DataMember(Name = "title")]
        public string Title { get; set; }

        [DataMember(Name = "trackno")]
        public int Trackno { get; set; }

        [DataMember(Name = "disc")]
        public int Disc { get; set; }

        [DataMember(Name = "album")]
        public string Album { get; set; }

        [DataMember(Name = "album_artist")]
        public string AlbumArtist { get; set; }

        [DataMember(Name = "genre")]
        public string Genre { get; set; }

        [DataMember(Name = "year")]
        public string Year { get; set; }

        public static bool operator ==(Track left, Track right)
        {
            return left?.Equals(right) ?? ReferenceEquals(right, null);
        }

        public static bool operator !=(Track left, Track right)
        {
            return !(left == right);
        }

        public static bool operator <(Track left, Track right)
        {
            return ReferenceEquals(left, null) ? !ReferenceEquals(right, null) : left.CompareTo(right) < 0;
        }

        public static bool operator <=(Track left, Track right)
        {
            return ReferenceEquals(left, null) || left.CompareTo(right) <= 0;
        }

        public static bool operator >(Track left, Track right)
        {
            return !ReferenceEquals(left, null) && left.CompareTo(right) > 0;
        }

        public static bool operator >=(Track left, Track right)
        {
            return ReferenceEquals(left, null) ? ReferenceEquals(right, null) : left.CompareTo(right) >= 0;
        }

        public bool Equals(Track other)
        {
            return other != null && other.Artist.Equals(Artist, StringComparison.InvariantCultureIgnoreCase) &&
                   other.Title.Equals(Title, StringComparison.InvariantCultureIgnoreCase);
        }

        public int CompareTo(Track other)
        {
            return other == null ? 1 : Trackno.CompareTo(other.Trackno);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Track);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Src != null ? Src.GetHashCode() : 0;
        }
    }
}
