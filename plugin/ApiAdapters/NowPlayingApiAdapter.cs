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

        public NowPlayingTrack GetPlayingTrackInfoLegacy()
        {
            var url = _api.NowPlaying_GetFileUrl().Cleanup();
            var title = _api.NowPlaying_GetFileTag(MetaDataType.TrackTitle).Cleanup();

            var nowPlayingTrack = new NowPlayingTrack
            {
                Artist = _api.NowPlaying_GetFileTag(MetaDataType.Artist).Cleanup(),
                Album = _api.NowPlaying_GetFileTag(MetaDataType.Album).Cleanup(),
                Year = _api.NowPlaying_GetFileTag(MetaDataType.Year).Cleanup()
            };
            nowPlayingTrack.SetTitle(title, url);
            return nowPlayingTrack;
        }

        public NowPlayingTrackV2 GetPlayingTrackInfo()
        {
            var url = _api.NowPlaying_GetFileUrl().Cleanup();
            var title = _api.NowPlaying_GetFileTag(MetaDataType.TrackTitle).Cleanup();

            var nowPlayingTrack = new NowPlayingTrackV2
            {
                Artist = _api.NowPlaying_GetFileTag(MetaDataType.Artist).Cleanup(),
                Album = _api.NowPlaying_GetFileTag(MetaDataType.Album).Cleanup(),
                Year = _api.NowPlaying_GetFileTag(MetaDataType.Year).Cleanup(),
                Path = url
            };
            nowPlayingTrack.SetTitle(title, url);
            return nowPlayingTrack;
        }

        public string SetRating(string rating)
        {
            var currentTrack = _api.NowPlaying_GetFileUrl();
            var decimalSeparator = Convert.ToChar(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
            try
            {
                rating = rating.Replace('.', decimalSeparator);
                float fRating;
                if (!float.TryParse(rating, out fRating))
                {
                    fRating = -1;
                }


                if (fRating >= 0 && fRating <= 5)
                {
                    var value = fRating.ToString(CultureInfo.CurrentCulture);
                    _api.Library_SetFileTag(currentTrack, MetaDataType.Rating, value);
                    _api.Library_CommitTagsToFile(currentTrack);
                    _api.Player_GetShowRatingTrack();
                    _api.MB_RefreshPanels();
                }
            }
            catch (Exception ex)
            {
                // Ignored exception? should log
            }

            return _api.Library_GetFileTag(currentTrack, MetaDataType.Rating).Replace(decimalSeparator, '.');
        }

        public LastfmStatus ChangeStatus(string action)
        {
            var hwnd = _api.MB_GetWindowHandle();
            var mb = (Form) Control.FromHandle(hwnd);

            if (action.Equals("toggle", StringComparison.OrdinalIgnoreCase))
            {
                if (GetLfmStatus() == LastfmStatus.Love || GetLfmStatus() == LastfmStatus.Ban)
                {
                    return (LastfmStatus) mb.Invoke(new Func<LastfmStatus>(SetLfmNormalStatus));
                }

                return (LastfmStatus) mb.Invoke(new Func<LastfmStatus>(SetLfmLoveStatus));
            }

            if (action.Equals("love", StringComparison.OrdinalIgnoreCase))
            {
                return (LastfmStatus) mb.Invoke(new Func<LastfmStatus>(SetLfmLoveStatus));
            }

            if (action.Equals("ban", StringComparison.OrdinalIgnoreCase))
            {
                return (LastfmStatus) mb.Invoke(new Func<LastfmStatus>(SetLfmLoveBan));
            }

            return GetLfmStatus();
        }

        private LastfmStatus SetLfmNormalStatus()
        {
            var fileUrl = _api.NowPlaying_GetFileUrl();
            var success = _api.Library_SetFileTag(fileUrl, MetaDataType.RatingLove, "lfm");
            return success ? LastfmStatus.Normal : GetLfmStatus();
        }

        private LastfmStatus SetLfmLoveStatus()
        {
            var fileUrl = _api.NowPlaying_GetFileUrl();
            var success = _api.Library_SetFileTag(fileUrl, MetaDataType.RatingLove, "Llfm");
            return success ? LastfmStatus.Love : GetLfmStatus();
        }

        private LastfmStatus SetLfmLoveBan()
        {
            var fileUrl = _api.NowPlaying_GetFileUrl();
            var success = _api.Library_SetFileTag(fileUrl, MetaDataType.RatingLove, "Blfm");
            return success ? LastfmStatus.Ban : GetLfmStatus();
        }

        public LastfmStatus GetLfmStatus()
        {
            LastfmStatus lastfmStatus;
            var apiReply = _api.NowPlaying_GetFileTag(MetaDataType.RatingLove);
            if (apiReply.Equals("L") || apiReply.Equals("lfm") || apiReply.Equals("Llfm"))
            {
                lastfmStatus = LastfmStatus.Love;
            }
            else if (apiReply.Equals("B") || apiReply.Equals("Blfm"))
            {
                lastfmStatus = LastfmStatus.Ban;
            }
            else
            {
                lastfmStatus = LastfmStatus.Normal;
            }
            return lastfmStatus;
        }
    }
}