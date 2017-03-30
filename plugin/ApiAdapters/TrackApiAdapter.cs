using System;
using System.Globalization;
using System.Windows.Forms;
using MusicBeeRemoteCore.Core.ApiAdapters;
using MusicBeeRemoteCore.Monitoring;
using MusicBeeRemoteCore.Remote;
using MusicBeeRemoteCore.Remote.Enumerations;
using MusicBeeRemoteCore.Remote.Model.Entities;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin.ApiAdapters
{
    public class TrackApiAdapter : ITrackApiAdapter
    {
        private readonly MusicBeeApiInterface _api;

        public TrackApiAdapter(MusicBeeApiInterface api)
        {
            _api = api;
        }

        public TrackTemporalnformation GetTemporalInformation()
        {
            var position = _api.Player_GetPosition();
            var duration = _api.NowPlaying_GetDuration();
            return new TrackTemporalnformation(position, duration);
        }

        public bool SeekTo(int position)
        {
            return _api.Player_SetPosition(position);
        }

        public string GetLyrics()
        {
            var embeddedLyrics = _api.NowPlaying_GetLyrics();
            if (!string.IsNullOrEmpty(embeddedLyrics))
            {
                return embeddedLyrics;
            }

            return _api.ApiRevision >= 17 ? _api.NowPlaying_GetDownloadedLyrics() : string.Empty;
        }

        public string GetCover()
        {
            var embeddedArtwork = _api.NowPlaying_GetArtwork();

            if (!string.IsNullOrEmpty(embeddedArtwork))
            {
                return embeddedArtwork;
            }

            if (_api.ApiRevision < 17) return string.Empty;

            var apiData = _api.NowPlaying_GetDownloadedArtwork();
            return !string.IsNullOrEmpty(apiData) ? apiData : string.Empty;
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

        public string GetRating()
        {
            var currentTrack = _api.NowPlaying_GetFileUrl();
            var decimalSeparator = Convert.ToChar(CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);
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