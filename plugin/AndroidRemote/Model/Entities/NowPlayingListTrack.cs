namespace MusicBeePlugin.AndroidRemote.Model.Entities
{
    class NowPlayingListTrack
    {
        public NowPlayingListTrack(string artist, string title, int position)
        {
            Position = position;
            Artist = artist;
            Title = title;
        }

        public NowPlayingListTrack()
        {
        }

        public string Artist { get; set; }

        public string Title { get; set; }

        public string Path { get; set; }

        public int Position { get; set; }
    }
}