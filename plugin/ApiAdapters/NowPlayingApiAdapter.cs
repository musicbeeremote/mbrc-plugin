using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using MusicBeeRemoteCore.Core.ApiAdapters;
using MusicBeeRemoteCore.Remote;
using MusicBeeRemoteCore.Remote.Enumerations;
using MusicBeeRemoteCore.Remote.Model.Entities;
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

        public bool MoveTrack(int from, int to)
        {
            int[] aFrom = {from};
            int dIn;
            if (from > to)
            {
                dIn = to - 1;
            }
            else
            {
                dIn = to;
            }

            return _api.NowPlayingList_MoveFiles(aFrom, dIn);
        }

        public bool PlayIndex(int index)
        {
            var success = false;
            string[] tracks = { };
            _api.NowPlayingList_QueryFilesEx(null, ref tracks);

            if (index >= 0 || index < tracks.Length)
            {
                success = _api.NowPlayingList_PlayNow(tracks[index]);
            }

            return success;
        }

        public bool RemoveIndex(int index)
        {
            return _api.NowPlayingList_RemoveAt(index);
        }

        public IEnumerable<NowPlaying> GetTracks(int offset = 0, int limit = 4000)
        {
            string[] tracks = { };
            _api.NowPlayingList_QueryFilesEx(null, ref tracks);

            return tracks.Select((path, position) =>
            {
                var artist = _api.Library_GetFileTag(path, MetaDataType.Artist);
                var title = _api.Library_GetFileTag(path, MetaDataType.TrackTitle);

                if (string.IsNullOrEmpty(title))
                {
                    var index = path.LastIndexOf('\\');
                    title = path.Substring(index + 1);
                }

                return new NowPlaying
                {
                    Artist = string.IsNullOrEmpty(artist) ? "Unknown Artist" : artist,
                    Title = title,
                    Position = position + 1,
                    Path = path
                };
            });
        }

        public IEnumerable<NowPlayingListTrack> GetTracksLegacy(int offset = 0, int limit = 5000)
        {
            string[] tracks = { };
            _api.NowPlayingList_QueryFilesEx(null, ref tracks);

            return tracks.Select((path, position) =>
            {
                var artist = _api.Library_GetFileTag(path, MetaDataType.Artist);
                var title = _api.Library_GetFileTag(path, MetaDataType.TrackTitle);

                if (string.IsNullOrEmpty(title))
                {
                    var index = path.LastIndexOf('\\');
                    title = path.Substring(index + 1);
                }

                return new NowPlayingListTrack
                {
                    Artist = string.IsNullOrEmpty(artist) ? "Unknown Artist" : artist,
                    Title = title,
                    Position = position + 1,
                    Path = path
                };
            });
        }
    }
}