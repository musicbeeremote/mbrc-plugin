namespace MusicBeePlugin.AndroidRemote.Entities
{
    class NowPlayingListTrack
    {
        private readonly int position;
        private readonly string artist;
        private readonly string title;

        public NowPlayingListTrack(string artist, string title, int position)
        {
            this.position = position;
            this.artist = artist;
            this.title = title;
        }

        public string Artist
        {
            get { return artist; }
        }

        public string Title
        {
            get { return title; }
        }

        public int Position
        {
            get { return position; }
        }

    }
}
