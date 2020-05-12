namespace MusicBeeRemote.Core.Model.Entities
{
    public abstract class NowPlayingTrackBase
    {
        private const string UnknownAlbum = "Unknown Album";
        private const string UnknownArtist = "Unknown Artist";

        /// <summary>
        /// Gets or sets the track artist.
        /// </summary>
        public abstract string Artist { get; set; }

        /// <summary>
        /// Gets or sets the track Title.
        /// </summary>
        public abstract string Title { get; set; }

        /// <summary>
        /// Gets or sets the track Album.
        /// </summary>
        public abstract string Album { get; set; }

        /// <summary>
        /// Gets or sets the track's album release year.
        /// </summary>
        public abstract string Year { get; set; }

        /// <summary>
        /// Sets the title of the track. If the title does not exist the title is inferred from the filename.
        /// </summary>
        /// <param name="title">The title of the track.</param>
        /// <param name="filePath">The path of the file in the file system.</param>
        public void SetTitle(string title, string filePath)
        {
            Title = GetTitleValue(title, filePath);
        }

        protected static string GetArtistText(string value)
        {
            return string.IsNullOrEmpty(value) ? UnknownArtist : value;
        }

        protected static string GetAlbumValue(string value)
        {
            return string.IsNullOrEmpty(value) ? UnknownAlbum : value;
        }

        private static string GetTitleValue(string title, string fileUrl)
        {
            return string.IsNullOrEmpty(title)
                ? string.IsNullOrEmpty(fileUrl) ? string.Empty : fileUrl.Substring(fileUrl.LastIndexOf('\\') + 1)
                : title;
        }
    }
}
