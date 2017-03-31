using System.Linq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Enumerations;
using MusicBeeRemote.Core.Model.Entities;

namespace MusicBeeRemote.Core.Support
{
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
            var trackList = tag == MetaTag.title && queue == QueueType.PlayNow
                ? new[] {query}
                : GetUrlsForTag(tag, query);

            _queueAdapter.QueueFiles(queue, trackList, query);
        }

        public string[] GetUrlsForTag(MetaTag tag, string query)
        {
            var filter = string.Empty;
            switch (tag)
            {
                case MetaTag.artist:
                    filter = FilterHelper.XmlFilter(new[] {"ArtistPeople"}, query, true);
                    break;
                case MetaTag.album:
                    filter = FilterHelper.XmlFilter(new[] {"Album"}, query, true);
                    break;
                case MetaTag.genre:
                    filter = FilterHelper.XmlFilter(new[] {"Genre"}, query, true);
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