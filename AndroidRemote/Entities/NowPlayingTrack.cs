namespace MusicBeePlugin.AndroidRemote.Entities
{
    using System;

    /// <summary>
    /// 
    /// </summary>
    public class NowPlayingTrack
    {
        private string artist;
        private string album;
        private string year;
        private string title;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="artist"></param>
        /// <param name="title"></param>
        /// <param name="album"></param>
        /// <param name="year"></param>
        public NowPlayingTrack(string artist,string title, string album, string year)
        {
            this.artist = artist;
            this.title = title;
            this.album = album;
            this.year = year;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="artist"></param>
        /// <param name="title"></param>
        public NowPlayingTrack(string artist, string title)
        {
            this.artist = artist;
            this.title = title;
            album = year = String.Empty;
        }

        /// <summary>
        /// 
        /// </summary>
        public NowPlayingTrack()
        {
            artist = title = album = year = String.Empty;
        }

        /// <summary>
        /// 
        /// </summary>
        public string Artist
        {
            get { return artist; }
            set
            {                
                artist = String.IsNullOrEmpty(value) ? "Unknown Artist" : value;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public string Title
        {
            get { return title; }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="title"></param>
        /// <param name="fileUrl"></param>
        public void SetTitle(string title, string fileUrl)
        {
            this.title = String.IsNullOrEmpty(title) ? 
                (String.IsNullOrEmpty(fileUrl) ? "" : fileUrl.Substring(fileUrl.LastIndexOf('\\') + 1)) :
                title;
        }

        /// <summary>
        /// 
        /// </summary>
        public string Album
        {
            get { return album; }
            set
            {
                album = String.IsNullOrEmpty(value) ? "Unknown Album" : value;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public string Year
        {
            get { return year; }
            set { year = value; }
        }
    }   
}
