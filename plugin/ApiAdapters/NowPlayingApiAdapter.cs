using System.Collections.Generic;
using System.Linq;
using MusicBeeRemote.Core.ApiAdapters;
using MusicBeeRemote.Core.Model.Entities;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.ApiAdapters
{
    /// <inheritdoc />
    public class NowPlayingApiAdapter : INowPlayingApiAdapter
    {
        private readonly MusicBeeApiInterface _api;

        /// <summary>
        /// Initializes a new instance of the <see cref="NowPlayingApiAdapter"/> class.
        /// </summary>
        /// <param name="api">The MusicBee API.</param>
        public NowPlayingApiAdapter(MusicBeeApiInterface api)
        {
            _api = api;
        }

        /// <inheritdoc />
        public bool MoveTrack(int startPosition, int endPosition)
        {
            int[] aFrom = { startPosition };
            int dIn;
            if (startPosition > endPosition)
            {
                dIn = endPosition - 1;
            }
            else
            {
                dIn = endPosition;
            }

            return _api.NowPlayingList_MoveFiles(aFrom, dIn);
        }

        /// <inheritdoc />
        public bool PlayIndex(int index)
        {
            var success = false;
            _api.NowPlayingList_QueryFilesEx(null, out var tracks);

            if (index >= 0 || index < tracks.Length)
            {
                success = _api.NowPlayingList_PlayNow(tracks[index - 1]);
            }

            return success;
        }

        /// <inheritdoc />
        public bool PlayPath(string path)
        {
            return _api.NowPlayingList_PlayNow(path);
        }

        /// <inheritdoc />
        public bool RemoveIndex(int index)
        {
            return _api.NowPlayingList_RemoveAt(index);
        }

        /// <inheritdoc />
        public IEnumerable<NowPlayingTrackInfo> GetTracks(int offset = 0, int limit = 5000)
        {
            _api.NowPlayingList_QueryFilesEx(null, out var tracks);

            return tracks.Select((path, position) =>
            {
                var artist = _api.Library_GetFileTag(path, MetaDataType.Artist);
                var title = _api.Library_GetFileTag(path, MetaDataType.TrackTitle);

                if (string.IsNullOrEmpty(title))
                {
                    var index = path.LastIndexOf('\\');
                    title = path.Substring(index + 1);
                }

                return new NowPlayingTrackInfo
                {
                    Artist = string.IsNullOrEmpty(artist) ? "Unknown Artist" : artist,
                    Title = title,
                    Position = position + 1,
                    Path = path,
                };
            }).ToList();
        }

        /// <inheritdoc />
        public IEnumerable<NowPlayingListTrack> GetTracksLegacy(int offset = 0, int limit = 5000)
        {
            _api.NowPlayingList_QueryFilesEx(null, out var tracks);

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
                    Path = path,
                };
            }).ToList();
        }
    }
}
