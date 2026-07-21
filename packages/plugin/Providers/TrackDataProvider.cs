using System;
using System.Collections.Generic;
using System.Globalization;
using MusicBeePlugin.Providers;
using MusicBeePlugin.Models;
using MusicBeePlugin.Ffi;
using MusicBeePlugin.Utilities;

namespace MusicBeePlugin.Providers
{
    /// <summary>
    ///     Data provider implementation for track and now-playing operations.
    ///     Contains all MusicBee-specific API logic including bulk tag retrieval and query operations.
    /// </summary>
    public class TrackDataProvider : ITrackDataProvider
    {
        private static readonly string[] SearchFields = { "ArtistPeople", "Title" };

        // The now-playing list item field set (read in one bulk Library_GetFileTags
        // per windowed track). The V4 wire codec drops album/album_artist for
        // Android; both carry the full set so V6+ can surface them (#329/#196).
        private static readonly Plugin.MetaDataType[] NowPlayingFields =
        {
            Plugin.MetaDataType.Artist, Plugin.MetaDataType.Album,
            Plugin.MetaDataType.AlbumArtist, Plugin.MetaDataType.TrackTitle,
        };

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

        public TrackInfo GetNowPlayingTrackInfo()
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

            // Raw values only: the V4 empty-field fallbacks (Unknown Artist/Album,
            // title -> file name) live in the Rust V4 wire codec now.
            return new TrackInfo
            {
                artist = SafeGetResult(success, results, 0),
                title = SafeGetResult(success, results, 1),
                album = SafeGetResult(success, results, 2),
                year = SafeGetResult(success, results, 3),
                path = GetNowPlayingFileUrl(),
            };
        }

        public TrackDetails GetNowPlayingTrackDetails()
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

            // Raw values only; the empty-albumArtist -> "Unknown Artist" fallback
            // lives in the Rust V4 wire codec now.
            var details = new TrackDetails
            {
                albumArtist = string.Empty,
                genre = string.Empty,
                trackNo = string.Empty,
                trackCount = string.Empty,
                discNo = string.Empty,
                discCount = string.Empty,
                grouping = string.Empty,
                publisher = string.Empty,
                ratingAlbum = string.Empty,
                composer = string.Empty,
                comment = string.Empty,
                encoder = string.Empty,
            };

            if (metadataSuccess && metadataResults.Length >= metadataFields.Length)
            {
                details.albumArtist = metadataResults[0].Cleanup();
                details.genre = metadataResults[1].Cleanup();
                details.trackNo = metadataResults[2].Cleanup();
                details.trackCount = metadataResults[3].Cleanup();
                details.discNo = metadataResults[4].Cleanup();
                details.discCount = metadataResults[5].Cleanup();
                details.grouping = metadataResults[6].Cleanup();
                details.publisher = metadataResults[7].Cleanup();
                details.ratingAlbum = metadataResults[8].Cleanup();
                details.composer = metadataResults[9].Cleanup();
                details.comment = metadataResults[10].Cleanup();
                details.encoder = metadataResults[11].Cleanup();
            }

            // Get file properties (these use different API methods)
            details.kind = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.Kind).Cleanup();
            details.format = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.Format).Cleanup();
            details.size = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.Size).Cleanup();
            details.channels = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.Channels).Cleanup();
            details.sampleRate = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.SampleRate).Cleanup();
            details.bitrate = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.Bitrate).Cleanup();
            details.dateModified = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.DateModified).Cleanup();
            details.dateAdded = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.DateAdded).Cleanup();
            details.lastPlayed = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.LastPlayed).Cleanup();
            details.playCount = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.PlayCount).Cleanup();
            details.skipCount = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.SkipCount).Cleanup();
            details.duration = _api.NowPlaying_GetFileProperty(Plugin.FilePropertyType.Duration).Cleanup();

            return details;
        }

        public PlaybackPositionResponse GetPlaybackPosition()
        {
            var current = _api.Player_GetPosition();
            var total = _api.NowPlaying_GetDuration();
            return new PlaybackPositionResponse { current = current, total = total };
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

        public string GetSyncedLyrics()
        {
            // Prefer real synchronized (LRC) lyrics for the current file, then
            // unsynchronised, before the paramless now-playing lyrics - a track can
            // have synced LRC the paramless call does not return (#113).
            var url = _api.NowPlaying_GetFileUrl();
            if (!string.IsNullOrEmpty(url) && _api.Library_GetLyrics != null)
            {
                var synced = _api.Library_GetLyrics(url, Plugin.LyricsType.Synchronised);
                if (!string.IsNullOrEmpty(synced))
                    return synced;

                var unsynced = _api.Library_GetLyrics(url, Plugin.LyricsType.UnSynchronised);
                if (!string.IsNullOrEmpty(unsynced))
                    return unsynced;
            }

            return GetNowPlayingLyrics();
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

        public IEnumerable<NowPlayingListTrack> GetNowPlayingListOrdered(int offset, int limit)
        {
            if (!_api.NowPlayingList_QueryFiles(null))
                yield break;

            // Walk the ordered sequence (current track forward) exactly as before,
            // but read tags ONLY inside the window [offset, offset+limit). Items
            // before the window advance the index cursor without a tag read, so a
            // page costs O(limit) tag reads, not O(list).
            var windowEnd = limit > 0 ? (long)offset + limit : long.MaxValue;
            var position = 1;
            var itemIndex = _api.NowPlayingList_GetCurrentIndex();
            while (position <= windowEnd)
            {
                var trackPath = _api.NowPlayingList_GetListFileUrl(itemIndex);
                if (string.IsNullOrEmpty(trackPath))
                    break;

                if (position > offset)
                {
                    var success = _api.Library_GetFileTags(trackPath, NowPlayingFields, out var results);
                    // Raw values; the empty title -> file name fallback lives in the
                    // Rust V4 wire codec now. position is the actual list index.
                    yield return new NowPlayingListTrack
                    {
                        artist = SafeGetResult(success, results, 0),
                        album = SafeGetResult(success, results, 1),
                        album_artist = SafeGetResult(success, results, 2),
                        title = SafeGetResult(success, results, 3),
                        position = itemIndex,
                        path = trackPath
                    };
                }

                itemIndex = _api.NowPlayingList_GetNextIndex(position);
                position++;
            }
        }

        public IEnumerable<NowPlayingListTrack> GetNowPlayingListPage(int offset, int limit)
        {
            if (!_api.NowPlayingList_QueryFiles(null))
                yield break;

            // Advance the list cursor sequentially, but read tags ONLY inside the
            // window [offset, offset+limit). Skipped items still pull the (cheap)
            // path string to move the cursor; the expensive tag read is bounded to
            // the page. Ordering is sequential (Android variant).
            var windowEnd = limit > 0 ? (long)offset + limit : long.MaxValue;
            var position = 1;
            while (position <= windowEnd)
            {
                var trackPath = _api.NowPlayingList_QueryGetNextFile();
                if (string.IsNullOrEmpty(trackPath))
                    break;

                if (position > offset)
                {
                    var success = _api.Library_GetFileTags(trackPath, NowPlayingFields, out var results);
                    // Raw values; the V4 empty-artist -> "Unknown Artist" and empty
                    // title -> file name fallbacks live in the Rust V4 wire codec.
                    yield return new NowPlayingListTrack
                    {
                        artist = SafeGetResult(success, results, 0),
                        album = SafeGetResult(success, results, 1),
                        album_artist = SafeGetResult(success, results, 2),
                        title = SafeGetResult(success, results, 3),
                        position = position,
                        path = trackPath
                    };
                }

                position++;
            }
        }

        public int GetNowPlayingListCount()
        {
            // Paths-only enumeration - the whole list's URLs in one call, no tag
            // reads - just for the page's reported total.
            return _api.NowPlayingList_QueryFilesEx(null, out var files) && files != null
                ? files.Length
                : 0;
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
