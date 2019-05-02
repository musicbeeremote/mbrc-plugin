using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Model.Entities;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.ApiAdapters
{
    public class NowPlayingApiAdapter : INowPlayingApiAdapter
    {
        private readonly MusicBeeApiInterface _api;

        public NowPlayingApiAdapter(MusicBeeApiInterface api)
        {
            _api = api;
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
            string[] tracks;
            _api.NowPlayingList_QueryFilesEx(null, out tracks);

            if (index >= 0 || index < tracks.Length)
            {
                success = _api.NowPlayingList_PlayNow(tracks[index - 1]);
            }

            return success;
        }

        public bool PlayPath(string path)
        {
            return _api.NowPlayingList_PlayNow(path);
        }

        public bool RemoveIndex(int index)
        {
            return _api.NowPlayingList_RemoveAt(index);
        }

        public IEnumerable<NowPlaying> GetTracks(int offset = 0, int limit = 4000)
        {
            string[] tracks;
            _api.NowPlayingList_QueryFilesEx(null, out tracks);

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
            }).ToList();
        }

        public IEnumerable<NowPlayingListTrack> GetTracksLegacy(int offset = 0, int limit = 5000)
        {
            string[] tracks;
            _api.NowPlayingList_QueryFilesEx(null, out tracks);

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
            }).ToList();
        }
    }
}