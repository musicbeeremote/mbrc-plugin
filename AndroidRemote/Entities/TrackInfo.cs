namespace MusicBeePlugin.AndroidRemote.Entities
{
    using System;
    using System.Security;
    using Networking;
    using Utilities;

    /// <summary>
    /// 
    /// </summary>
    public class TrackInfo
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
        public TrackInfo(string artist,string title, string album, string year)
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
        public TrackInfo(string artist, string title)
        {
            this.artist = artist;
            this.title = title;
            album = year = String.Empty;
        }

        /// <summary>
        /// 
        /// </summary>
        public TrackInfo()
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
                artist = SecurityElement.Escape(String.IsNullOrEmpty(value) ? "Unknown Artist" : value);
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
            this.title = String.IsNullOrEmpty(title) ? fileUrl.Substring(fileUrl.LastIndexOf('\\') + 1) : title;
        }

        /// <summary>
        /// 
        /// </summary>
        public string Album
        {
            get { return album; }
            set
            {
                album = SecurityElement.Escape(String.IsNullOrEmpty(value) ? "Unknown Album" : value);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public string Year
        {
            get { return year; }
            set { year = SecurityElement.Escape(value); }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public string ToXmlString()
        {
            string sInfo = XmlCreator.Create(Constants.Artist, Artist, false, false);
            sInfo += XmlCreator.Create(Constants.Title, Title, false, false);
            sInfo += XmlCreator.Create(Constants.Album, Album, false, false);
            sInfo += XmlCreator.Create(Constants.Year, Year, false, false);
            return sInfo;
        }
    }   
}
