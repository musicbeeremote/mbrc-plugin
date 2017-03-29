using System;
using System.Linq;
using MusicBeeRemoteCore.Core.ApiAdapters;
using static MusicBeePlugin.Plugin;
using static MusicBeeRemoteCore.Core.Support.FilterHelper;

namespace MusicBeePlugin.ApiAdapters
{
    public class NowPlayingApiAdapter : INowPlayingApiAdapter
    {
        private readonly MusicBeeApiInterface _api;

        public NowPlayingApiAdapter(MusicBeeApiInterface api)
        {
            _api = api;
        }

        public bool PlayMatchingTrack(string query)
        {
            string[] tracks = { };
            _api.NowPlayingList_QueryFilesEx(XmlFilter(new[] {"ArtistPeople", "Title"}, query, false), ref tracks);

            return (from currentTrack in tracks
                let artist = _api.Library_GetFileTag(currentTrack, MetaDataType.Artist)
                let title = _api.Library_GetFileTag(currentTrack, MetaDataType.TrackTitle)
                let noTitleMatch = title.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
                let noArtistMatch = artist.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
                where !noTitleMatch || !noArtistMatch
                select _api.NowPlayingList_PlayNow(currentTrack)).FirstOrDefault();
        }
    }
}