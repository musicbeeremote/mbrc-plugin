using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Enumerations;
using MusicBeePlugin.Models.Entities;
using MusicBeePlugin.Utilities.Common;
using MusicBeePlugin.Utilities.Data;

namespace MusicBeePlugin.DataProviders
{
    /// <summary>
    ///     Data provider implementation for track and now-playing operations.
    ///     Contains all MusicBee-specific API logic including bulk tag retrieval and query operations.
    /// </summary>
    public class TrackDataProvider : ITrackDataProvider
    {
        /// <summary>
        ///     Maximum number of tracks to return in legacy playlist queries.
        /// </summary>
        private const int MaxLegacyPlaylistSize = 5000;

        private static readonly string[] SearchFields = { "ArtistPeople", "Title" };
        private readonly Plugin.MusicBeeApiInterface _api;

        public TrackDataProvider(Plugin.MusicBeeApiInterface api)
        {
            _api = api;
        }

        #region Now Playing Track Info

        public string GetNowPlayingFileUrl()
        {
            return _api.NowPlaying_GetFileUrl();
        }

        public NowPlayingTrackV2 GetNowPlayingTrackInfo()
        {
            // Use bulk API to get basic track info efficiently
            var fields = new[]
            {
                Plugin.MetaDataType.Artist,
                Plugin.MetaDataType.TrackTitle,
                Plugin.MetaDataType.Album,
                Plugin.MetaDataType.Year
            };

            var results = new string[fields.Length];
            var success = _api.NowPlaying_GetFileTags(fields, out results);

            var fileUrl = GetNowPlayingFileUrl();
            var track = new NowPlayingTrackV2
            {
                Artist = SafeGetResult(success, results, 0),
                Album = SafeGetResult(success, results, 2),
                Year = SafeGetResult(success, results, 3),
                Path = fileUrl
            };

            // SetTitle handles the logic for title vs filename fallback
            var title = SafeGetResult(success, results, 1);
            track.SetTitle(title, fileUrl);

            return track;
        }

        public NowPlayingDetails GetNowPlayingTrackDetails()
        {
            // Use bulk API to get comprehensive track details efficiently
            var metadataFields = new[]
            {
                Plugin.MetaDataType.AlbumArtist,
                Plugin.MetaDataType.Genre,
                Plugin.MetaDataType.TrackNo,
                Plugin.MetaDataType.TrackCount,
                Plugin.MetaDataType.DiscNo,
                Plugin.MetaDataType.DiscCount,
                Plugin.MetaDataType.Grouping,
                Plugin.MetaDataType.Publisher,
                Plugin.MetaDataType.RatingAlbum,
                Plugin.MetaDataType.Composer,
                Plugin.MetaDataType.Comment,
                Plugin.MetaDataType.Encoder
            };

            var metadataResults = new string[metadataFields.Length];
            var metadataSuccess = _api.NowPlaying_GetFileTags(metadataFields, out metadataResults);

            var details = new NowPlayingDetails();
            if (metadataSuccess && metadataResults.Length >= metadataFields.Length)
            {
                details.AlbumArtist = metadataResults[0].Cleanup();
                details.Genre = metadataResults[1].Cleanup();
                details.TrackNo = metadataResults[2].Cleanup();
                details.TrackCount = metadataResults[3].Cleanup();
                details.DiscNo = metadataResults[4].Cleanup();
                details.DiscCount = metadataResults[5].Cleanup();
                details.Grouping = metadataResults[6].Cleanup();
                details.Publisher = metadataResults[7].Cleanup();
                details.RatingAlbum = metadataResults[8].Cleanup();
                details.Composer = metadataResults[9].Cleanup();
                details.Comment = metadataResults[10].Cleanup();
                details.Encoder = metadataResults[11].Cleanup();
            }

            // Get file properties (these use different API methods)
            details.Kind = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.Kind).Cleanup();
            details.Format = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.Format).Cleanup();
            details.Size = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.Size).Cleanup();
            details.Channels = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.Channels).Cleanup();
            details.SampleRate = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.SampleRate).Cleanup();
            details.Bitrate = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.Bitrate).Cleanup();
            details.DateModified = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.DateModified).Cleanup();
            details.DateAdded = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.DateAdded).Cleanup();
            details.LastPlayed = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.LastPlayed).Cleanup();
            details.PlayCount = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.PlayCount).Cleanup();
            details.SkipCount = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.SkipCount).Cleanup();
            details.Duration = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.Duration).Cleanup();

            return details;
        }

        public PlaybackPosition GetPlaybackPosition()
        {
            var current = _api.Player_GetPosition();
            var total = _api.NowPlaying_GetDuration();
            return new PlaybackPosition(current, total);
        }

        #endregion

        #region Media Content

        public string GetNowPlayingArtwork()
        {
            return _api.NowPlaying_GetArtwork();
        }

        public string GetNowPlayingDownloadedArtwork()
        {
            return _api.ApiRevision >= 17 ? _api.NowPlaying_GetDownloadedArtwork() : string.Empty;
        }

        public string GetNowPlayingLyrics()
        {
            var lyrics = _api.NowPlaying_GetLyrics();

            if (!string.IsNullOrEmpty(lyrics))
                return lyrics;

            return _api.ApiRevision >= 17 ? _api.NowPlaying_GetDownloadedLyrics() : string.Empty;
        }

        #endregion

        #region Now Playing List Operations

        public string SearchNowPlayingList(string query, SearchSource searchSource)
        {
            var filter = XmlFilterHelper.CreateFilter(SearchFields, query, false, searchSource);
            if (!_api.NowPlayingList_QueryFiles(filter))
                return string.Empty;

            while (true)
            {
                var currentTrack = _api.NowPlayingList_QueryGetNextFile();
                if (string.IsNullOrEmpty(currentTrack))
                    break;

                // Use bulk API for efficiency - get both artist and title in one call
                var fields = new[] { Plugin.MetaDataType.Artist, Plugin.MetaDataType.TrackTitle };
                var results = new string[2];
                var success = _api.Library_GetFileTags(currentTrack, fields, out results);

                if (!success || results.Length < 2)
                    continue;

                var artist = results[0] ?? string.Empty;
                var title = results[1] ?? string.Empty;

                if (title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    artist.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    return currentTrack;
            }

            return null;
        }

        public IEnumerable<NowPlayingListTrack> GetNowPlayingListLegacy()
        {
            if (!_api.NowPlayingList_QueryFiles(null))
                yield break;

            var position = 1;
            while (position <= MaxLegacyPlaylistSize)
            {
                var trackPath = _api.NowPlayingList_QueryGetNextFile();
                if (string.IsNullOrEmpty(trackPath))
                    break;

                // Use bulk API for efficiency
                var fields = new[] { Plugin.MetaDataType.Artist, Plugin.MetaDataType.TrackTitle };
                var results = new string[2];
                var success = _api.Library_GetFileTags(trackPath, fields, out results);

                var artist = SafeGetResult(success, results, 0);
                var title = SafeGetResult(success, results, 1);

                if (string.IsNullOrEmpty(title))
                    title = Path.GetFileName(trackPath);

                yield return new NowPlayingListTrack
                {
                    Artist = string.IsNullOrEmpty(artist) ? "Unknown Artist" : artist,
                    Title = title,
                    Position = position,
                    Path = trackPath
                };
                position++;
            }
        }

        public IEnumerable<NowPlaying> GetNowPlayingListOrdered(int offset, int limit)
        {
            if (!_api.NowPlayingList_QueryFiles(null))
                yield break;

            var position = 1;
            var itemIndex = _api.NowPlayingList_GetCurrentIndex();
            while (position <= limit)
            {
                var trackPath = _api.NowPlayingList_GetListFileUrl(itemIndex);

                if (string.IsNullOrEmpty(trackPath))
                    break;

                // Use bulk API for efficiency
                var fields = new[]
                {
                    Plugin.MetaDataType.Artist,
                    Plugin.MetaDataType.Album,
                    Plugin.MetaDataType.AlbumArtist,
                    Plugin.MetaDataType.TrackTitle
                };
                var results = new string[4];
                var success = _api.Library_GetFileTags(trackPath, fields, out results);

                var artist = SafeGetResult(success, results, 0);
                var album = SafeGetResult(success, results, 1);
                var albumArtist = SafeGetResult(success, results, 2);
                var title = SafeGetResult(success, results, 3);

                if (string.IsNullOrEmpty(title))
                    title = Path.GetFileName(trackPath);

                yield return new NowPlaying
                {
                    Artist = artist,
                    Album = album,
                    AlbumArtist = albumArtist,
                    Title = title,
                    Position = itemIndex,
                    Path = trackPath
                };

                itemIndex = _api.NowPlayingList_GetNextIndex(position);
                position++;
            }
        }

        public IEnumerable<NowPlaying> GetNowPlayingListPage(int offset, int limit)
        {
            if (!_api.NowPlayingList_QueryFiles(null))
                yield break;

            var position = 1;
            while (true)
            {
                var trackPath = _api.NowPlayingList_QueryGetNextFile();
                if (string.IsNullOrEmpty(trackPath))
                    break;

                // Use bulk API for efficiency
                var fields = new[] { Plugin.MetaDataType.Artist, Plugin.MetaDataType.TrackTitle };
                var results = new string[2];
                var success = _api.Library_GetFileTags(trackPath, fields, out results);

                var artist = SafeGetResult(success, results, 0);
                var title = SafeGetResult(success, results, 1);

                if (string.IsNullOrEmpty(title))
                    title = Path.GetFileName(trackPath);

                yield return new NowPlaying
                {
                    Artist = string.IsNullOrEmpty(artist) ? "Unknown Artist" : artist,
                    Title = title,
                    Position = position,
                    Path = trackPath
                };
                position++;
            }
        }

        #endregion

        #region Rating Operations

        public string GetNowPlayingRating()
        {
            var currentTrack = _api.NowPlaying_GetFileUrl();
            if (string.IsNullOrEmpty(currentTrack))
                return string.Empty;

            var rating = _api.Library_GetFileTag(currentTrack, Plugin.MetaDataType.Rating);

            // Convert decimal separator to dots for consistency
            var decimalSeparator = Convert.ToChar(CultureInfo.CurrentCulture.NumberFormat
                .NumberDecimalSeparator, CultureInfo.InvariantCulture);
            return rating.Replace(decimalSeparator, '.');
        }

        public bool SetNowPlayingRating(string rating)
        {
            var currentTrack = _api.NowPlaying_GetFileUrl();
            if (string.IsNullOrEmpty(currentTrack))
                return false;

            try
            {
                var decimalSeparator = Convert.ToChar(CultureInfo.CurrentCulture
                    .NumberFormat.NumberDecimalSeparator, CultureInfo.InvariantCulture);
                rating = rating.Replace('.', decimalSeparator);

                if (!float.TryParse(rating, out var fRating))
                    fRating = -1;

                // Only set rating if it's valid (0-5) or empty string
                if ((fRating >= 0 && fRating <= 5) || rating == "")
                {
                    var value = rating == ""
                        ? rating.ToString(CultureInfo.CurrentCulture)
                        : fRating.ToString(CultureInfo.CurrentCulture);

                    var success = _api.Library_SetFileTag(currentTrack, Plugin.MetaDataType.Rating, value);
                    if (!success) return false;

                    var commitSuccess = _api.Library_CommitTagsToFile(currentTrack);
                    _api.Player_GetShowRatingTrack(); // Returns void - refresh UI
                    _api.MB_RefreshPanels(); // Returns void - refresh UI
                    return commitSuccess;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Last.fm Operations

        public LastfmStatus GetNowPlayingLastfmStatus()
        {
            var apiReply = _api.NowPlaying_GetFileTag(Plugin.MetaDataType.RatingLove);
            if (apiReply.Equals("L", StringComparison.Ordinal) ||
                apiReply.Equals("lfm", StringComparison.Ordinal) ||
                apiReply.Equals("Llfm", StringComparison.Ordinal))
                return LastfmStatus.Love;
            if (apiReply.Equals("B", StringComparison.Ordinal) ||
                apiReply.Equals("Blfm", StringComparison.Ordinal))
                return LastfmStatus.Ban;
            return LastfmStatus.Normal;
        }

        public bool SetNowPlayingLastfmStatus(LastfmStatus status)
        {
            var fileUrl = _api.NowPlaying_GetFileUrl();
            string tagValue;

            switch (status)
            {
                case LastfmStatus.Love:
                    tagValue = "Llfm";
                    break;
                case LastfmStatus.Ban:
                    tagValue = "Blfm";
                    break;
                case LastfmStatus.Normal:
                    tagValue = "lfm";
                    break;
                default:
                    return false;
            }

            return _api.Library_SetFileTag(fileUrl, Plugin.MetaDataType.RatingLove, tagValue);
        }

        #endregion

        #region Tag Operations

        public bool SetTrackTag(string fileUrl, string tagName, string value)
        {
            var metadataType = GetMetaDataTypeFromTagName(tagName);
            if (metadataType == null)
                return false;

            return _api.Library_SetFileTag(fileUrl, metadataType.Value, value);
        }

        public bool CommitTrackTags(string fileUrl)
        {
            return _api.Library_CommitTagsToFile(fileUrl);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        ///     Safely retrieves a result from a string array with bounds checking.
        /// </summary>
        private static string SafeGetResult(bool success, string[] results, int index)
        {
            return success && results != null && results.Length > index
                ? results[index]
                : string.Empty;
        }

        private static Plugin.MetaDataType? GetMetaDataTypeFromTagName(string tagName)
        {
            switch (tagName)
            {
                case "TrackTitle":
                    return Plugin.MetaDataType.TrackTitle;
                case "Artist":
                    return Plugin.MetaDataType.Artist;
                case "Album":
                    return Plugin.MetaDataType.Album;
                case "AlbumArtist":
                    return Plugin.MetaDataType.AlbumArtist;
                case "Genre":
                    return Plugin.MetaDataType.Genre;
                case "Year":
                    return Plugin.MetaDataType.Year;
                case "Composer":
                    return Plugin.MetaDataType.Composer;
                case "Comment":
                    return Plugin.MetaDataType.Comment;
                case "Lyrics":
                    return Plugin.MetaDataType.Lyrics;
                default:
                    return null;
            }
        }

        #endregion
    }
}
