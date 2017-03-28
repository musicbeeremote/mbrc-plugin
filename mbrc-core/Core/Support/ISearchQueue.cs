using System.Linq;
using MusicBeeRemoteCore.ApiAdapters;
using MusicBeeRemoteCore.Core.ApiAdapters;
using MusicBeeRemoteCore.Remote.Enumerations;
using MusicBeeRemoteCore.Remote.Model.Entities;
using static MusicBeeRemoteCore.Core.Support.FilterHelper;

namespace MusicBeeRemoteCore.Core.Support
{
    public interface ISearchQueue
    {
        /// <summary>
        /// Implements the legacy (before version 1.0.0) queuing mechanism that used search in
        /// and keywords in order to supply queuing functionality
        /// </summary>
        /// <param name="queue">The actual queue action (can be next, last, now)</param>
        /// <param name="tag">The type of tag that will be queued</param>
        /// <param name="query">The tag value that will be queued, or the track if it is a single track</param>
        void RequestQueueFiles(QueueType queue, MetaTag tag, string query);
    }

    class SearchQueue : ISearchQueue
    {
        private readonly IQueueAdapter _queueAdapter;
        private readonly ILibraryApiAdapter _libraryApiAdapter;

        public SearchQueue(IQueueAdapter queueAdapter, ILibraryApiAdapter libraryApiAdapter)
        {
            _queueAdapter = queueAdapter;
            _libraryApiAdapter = libraryApiAdapter;
        }

        public void RequestQueueFiles(QueueType queue, MetaTag tag, string query)
        {
            string[] trackList;
            if (tag == MetaTag.title && queue == QueueType.PlayNow)
            {
                trackList = new[] {query};
            }
            else
            {
                trackList = GetUrlsForTag(tag, query);
            }

            _queueAdapter.QueueFiles(queue, trackList, query);
        }

        public string[] GetUrlsForTag(MetaTag tag, string query)
        {
            var filter = string.Empty;
            switch (tag)
            {
                case MetaTag.artist:
                    filter = XmlFilter(new[] {"ArtistPeople"}, query, true);
                    break;
                case MetaTag.album:
                    filter = XmlFilter(new[] {"Album"}, query, true);
                    break;
                case MetaTag.genre:
                    filter = XmlFilter(new[] {"Genre"}, query, true);
                    break;
                case MetaTag.title:
                    filter = "";
                    break;
            }


            var files = _libraryApiAdapter.QueryFiles(filter).ToList();
            var list = files.Select(file => new MetaData
                {
                    File = file,
                    Artist = _libraryApiAdapter.GetArtistForTrack(file),
                    AlbumArtist = _libraryApiAdapter.GetAlbumArtistForTrack(file),
                    Album = _libraryApiAdapter.GetAlbumForTrack(file),
                    Title = _libraryApiAdapter.GetTitleForTrack(file),
                    Genre = _libraryApiAdapter.GetGenreForTrack(file),
                    Year = _libraryApiAdapter.GetYearForTrack(file),
                    TrackNo = _libraryApiAdapter.GetTrackNumber(file),
                    Disc = _libraryApiAdapter.GetDiskNumber(file)
                })
                .ToList();
            list.Sort();

            return list.Select(r => r.File).ToArray();
        }
    }
}