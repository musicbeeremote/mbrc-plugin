namespace MusicBeePlugin.AndroidRemote.Model.Entities
{
    public abstract class NowPlayingTrackBase
    {
        private const string UnknownAlbum = "Unknown Album";
        private const string UnknownArtist = "Unknown Artist";

        /// <summary>
        /// </summary>
        public abstract string Artist { get; set; }

        /// <summary>
        /// </summary>
        public abstract string Title { get; set; }

        /// <summary>
        /// </summary>
        public abstract string Album { get; set; }

        /// <summary>
        /// </summary>
        public abstract string Year { get; set; }

        protected static string GetArtistText(string value)
        {
            return string.IsNullOrEmpty(value) ? UnknownArtist : value;
        }

        /// <summary>
        /// </summary>
        /// <param name="title"></param>
        /// <param name="fileUrl"></param>
        public void SetTitle(string title, string fileUrl)
        {
            Title = GetTitleValue(title, fileUrl);
        }

        private static string GetTitleValue(string title, string fileUrl)
        {
            return string.IsNullOrEmpty(title)
                ? string.IsNullOrEmpty(fileUrl) ? "" : fileUrl.Substring(fileUrl.LastIndexOf('\\') + 1)
                : title;
        }

        protected static string GetAlbumValue(string value)
        {
            return string.IsNullOrEmpty(value) ? UnknownAlbum : value;
        }
    }
}