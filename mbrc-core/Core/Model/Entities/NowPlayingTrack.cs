namespace MusicBeeRemote.Core.Model.Entities
{

    public class NowPlayingTrack : NowPlayingTrackBase
    {
        private string _artist;
        private string _album;

        public NowPlayingTrack(string artist, string title, string album, string year)
        {
            _artist = artist;
            Title = title;
            _album = album;
            Year = year;
        }

        public NowPlayingTrack(string artist, string title)
        {
            _artist = artist;
            Title = title;
            _album = Year = string.Empty;
        }

        public NowPlayingTrack()
        {
            _artist = Title = _album = Year = string.Empty;
        }

        public override string Artist
        {
            get { return _artist; }
            set { _artist = GetArtistText(value); }
        }

        public override string Title { get; set; }

        public override string Album
        {
            get { return _album; }
            set { _album = GetAlbumValue(value); }
        }

        public sealed override string Year { get; set; }
    }
}