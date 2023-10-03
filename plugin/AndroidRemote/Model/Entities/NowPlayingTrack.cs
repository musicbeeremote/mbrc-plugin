namespace MusicBeePlugin.AndroidRemote.Model.Entities
{
    public class NowPlayingTrack : NowPlayingTrackBase
    {
        private string _album;
        private string _artist;

        public NowPlayingTrack()
        {
            _artist = Title = _album = Year = string.Empty;
        }

        public override string Artist
        {
            get => _artist;
            set => _artist = GetArtistText(value);
        }

        public sealed override string Title { get; set; }

        public override string Album
        {
            get => _album;
            set => _album = GetAlbumValue(value);
        }

        public sealed override string Year { get; set; }
    }
}