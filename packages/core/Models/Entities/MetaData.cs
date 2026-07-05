using System;
using System.IO;
using System.Runtime.Serialization;

namespace MusicBeePlugin.Models.Entities
{
    /// <summary>
    ///     Class MetaData.
    ///     Represents a packet payload for library meta data.
    /// </summary>
    [DataContract]
    public sealed class MetaData : IComparable<MetaData>
    {
        private const string Empty = @"[Empty]";
        private string _album;
        private string _artist;
        private string _genre;
        private string _title;

        [IgnoreDataMember] public string File { get; set; }

        [DataMember(Name = "album")]
        public string Album
        {
            get => _album;
            set => _album = string.IsNullOrEmpty(value) ? Empty : value;
        }

        [DataMember(Name = "title")]
        public string Title
        {
            get => _title;
            set =>
                _title = !string.IsNullOrEmpty(value)
                    ? value
                    : string.IsNullOrEmpty(File)
                        ? string.Empty
                        : Path.GetFileName(File);
        }

        [DataMember(Name = "genre")]
        public string Genre
        {
            get => _genre;
            set => _genre = string.IsNullOrEmpty(value) ? Empty : value;
        }

        [DataMember(Name = "year")] public string Year { get; set; }

        [DataMember(Name = "track_no")] public string TrackNo { get; set; }

        [DataMember(Name = "hash")] public string Hash { get; set; }

        [DataMember(Name = "artist")]
        public string Artist
        {
            get => _artist;
            set => _artist = string.IsNullOrEmpty(value) ? Empty : value;
        }

        [DataMember(Name = "album_artist")] public string AlbumArtist { get; set; }

        [DataMember(Name = "disc")] public string Disc { get; set; }

        public int CompareTo(MetaData other)
        {
            if (other is null)
                return 1;
            if (!string.IsNullOrEmpty(AlbumArtist) && other.AlbumArtist != AlbumArtist)
                return string.Compare(AlbumArtist, other.AlbumArtist, StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(Album) && other.Album != Album)
                return string.Compare(Album, other.Album, StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(Disc) && other.Disc != Disc)
            {
                var thisDisc = int.TryParse(Disc, out var parsedThisDisc) ? parsedThisDisc : 0;
                var otherDisc = int.TryParse(other.Disc, out var parsedOtherDisc) ? parsedOtherDisc : 0;
                return thisDisc - otherDisc;
            }

            var thisTrack = int.TryParse(TrackNo, out var parsedThisTrack) ? parsedThisTrack : 0;
            var otherTrack = int.TryParse(other.TrackNo, out var parsedOtherTrack) ? parsedOtherTrack : 0;
            return thisTrack - otherTrack;
        }

        public override bool Equals(object obj)
        {
            if (obj is null)
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            return obj is MetaData other && CompareTo(other) == 0;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = AlbumArtist != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(AlbumArtist) : 0;
                hashCode = (hashCode * 397) ^ (Album != null ? StringComparer.OrdinalIgnoreCase.GetHashCode(Album) : 0);
                hashCode = (hashCode * 397) ^ (Disc != null ? Disc.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (TrackNo != null ? TrackNo.GetHashCode() : 0);
                return hashCode;
            }
        }

        public static bool operator ==(MetaData left, MetaData right)
        {
            if (left is null)
                return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(MetaData left, MetaData right)
        {
            return !(left == right);
        }

        public static bool operator <(MetaData left, MetaData right)
        {
            return left is null ? right is object : left.CompareTo(right) < 0;
        }

        public static bool operator <=(MetaData left, MetaData right)
        {
            return left is null || left.CompareTo(right) <= 0;
        }

        public static bool operator >(MetaData left, MetaData right)
        {
            return left is object && left.CompareTo(right) > 0;
        }

        public static bool operator >=(MetaData left, MetaData right)
        {
            return left is null ? right is null : left.CompareTo(right) >= 0;
        }
    }
}
