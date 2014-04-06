using System;
using System.Runtime.Serialization;

namespace MusicBeePlugin.AndroidRemote.Entities
{
    /// <summary>
    /// Class MetaData. 
    /// Represents a packet payload for library meta data.
    /// </summary>
    class MetaData : IComparable<MetaData>
    {
        private const String Empty = @"[Empty]";
        private string _file;
        private string _hash;
        private string _artist;
        private string _album_artist;
        private string _album;
        private string _title;
        private string _genre;
        private string _year;
        private string _disc;
        private string _track_no;

        [IgnoreDataMember]
        public string file
        {
            get { return _file; }
            set { _file = value; }
        }

        public string album
        {
            get { return _album; }
            set { _album = String.IsNullOrEmpty(value) ? Empty : value; }
        }

        public string title
        {
            get { return _title; }
            set
            {
                _title = !String.IsNullOrEmpty(value)
                    ? value
                    : (String.IsNullOrEmpty(_file)
                        ? String.Empty
                        : _file.Substring(_file.LastIndexOf('\\') + 1));
            }
        }

        public string genre
        {
            get { return _genre; }
            set { _genre = String.IsNullOrEmpty(value) ? Empty : value; }
        }

        public string year
        {
            get { return _year; }
            set { _year = value; }
        }

        public string track_no
        {
            get { return _track_no; }
            set { _track_no = value; }
        }

        public string hash
        {
            get { return _hash; }
            set { _hash = value; }
        }

        public string artist
        {
            get { return _artist; }
            set { _artist = String.IsNullOrEmpty(value) ? Empty : value; }
        }

        public string album_artist
        {
            get { return _album_artist; }
            set { _album_artist = value; }
        }

        public string disc
        {
            get { return _disc; }
            set { _disc = value; }
        }

        public int CompareTo(MetaData other)
        {
            if (!String.IsNullOrEmpty(album_artist) && other.album_artist != album_artist)
            {
                return String.Compare(album_artist, other.album_artist, StringComparison.OrdinalIgnoreCase);
            }
            if (!String.IsNullOrEmpty(album) && other.album != album)
            {
                return String.Compare(album, other.album, StringComparison.OrdinalIgnoreCase);
            }
            if (!String.IsNullOrEmpty(disc) && other.disc != disc)
            {
                int thisDisc;
                int otherDisc;
                int.TryParse(disc, out thisDisc);
                int.TryParse(other.disc, out otherDisc);
                return thisDisc - otherDisc;
            }

            int thisTrack;
            int otherTrack;
            int.TryParse(track_no, out thisTrack);
            int.TryParse(other.track_no, out otherTrack);
            return thisTrack - otherTrack;

        }
    }
}
