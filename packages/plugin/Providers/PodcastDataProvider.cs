using System;
using System.Collections.Generic;
using MusicBeePlugin.Ffi;
using MusicBeePlugin.Utilities;

namespace MusicBeePlugin.Providers
{
    /// <summary>
    ///     Podcast subscriptions and episodes, from MusicBee's `Podcasts_*` API.
    /// </summary>
    public class PodcastDataProvider : IPodcastDataProvider
    {
        /// <summary>
        ///     Which artwork of a subscription to read. MusicBee takes an index
        ///     here; 0 is the subscription's own feed image.
        /// </summary>
        private const int FeedArtwork = 0;

        private readonly Plugin.MusicBeeApiInterface _api;

        public PodcastDataProvider(Plugin.MusicBeeApiInterface api)
        {
            _api = api;
        }

        public Page<PodcastSubscription> GetSubscriptions(int offset, int limit)
        {
            var ids = SubscriptionIds();
            var page = new Page<PodcastSubscription>
            {
                offset = offset,
                limit = limit,
                total = ids.Length,
                data = new List<PodcastSubscription>(),
            };

            var start = offset > 0 ? offset : 0;
            var end = limit > 0 ? Math.Min((long)start + limit, ids.Length) : ids.Length;
            for (var i = start; i < end; i++)
            {
                var subscription = ReadSubscription(ids[i]);
                if (subscription != null)
                    page.data.Add(subscription);
            }

            return page;
        }

        public List<PodcastSubscription> GetSubscription(string id)
        {
            var found = new List<PodcastSubscription>();
            var subscription = ReadSubscription(id);
            if (subscription != null)
                found.Add(subscription);
            return found;
        }

        public Page<PodcastEpisode> GetEpisodes(string id, int offset, int limit)
        {
            var urls = EpisodeUrls(id);
            var page = new Page<PodcastEpisode>
            {
                offset = offset,
                limit = limit,
                total = urls.Length,
                data = new List<PodcastEpisode>(),
            };

            var start = offset > 0 ? offset : 0;
            var end = limit > 0 ? Math.Min((long)start + limit, urls.Length) : urls.Length;
            for (var i = start; i < end; i++)
            {
                var episode = ReadEpisode(id, i, urls);
                if (episode != null)
                    page.data.Add(episode);
            }

            return page;
        }

        public List<PodcastEpisode> GetEpisode(string id, int index)
        {
            var found = new List<PodcastEpisode>();
            var episode = ReadEpisode(id, index, EpisodeUrls(id));
            if (episode != null)
                found.Add(episode);
            return found;
        }

        public string GetSubscriptionArtwork(string id)
        {
            if (string.IsNullOrEmpty(id))
                return string.Empty;

            return _api.Podcasts_GetSubscriptionArtwork(id, FeedArtwork, out var imageData)
                   && imageData != null
                   && imageData.Length > 0
                ? Convert.ToBase64String(imageData)
                : string.Empty;
        }

        private string[] SubscriptionIds()
        {
            return _api.Podcasts_QuerySubscriptions(null, out var ids) && ids != null
                ? ids
                : Array.Empty<string>();
        }

        /// <summary>
        ///     A subscription's whole episode list as URLs. One call, no metadata
        ///     reads: it is both the episode count and what playing one needs.
        /// </summary>
        private string[] EpisodeUrls(string id)
        {
            if (string.IsNullOrEmpty(id))
                return Array.Empty<string>();

            return _api.Podcasts_GetSubscriptionEpisodes(id, out var urls) && urls != null
                ? urls
                : Array.Empty<string>();
        }

        private PodcastSubscription ReadSubscription(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (!_api.Podcasts_GetSubscription(id, out var fields) || fields == null)
                return null;

            return new PodcastSubscription
            {
                // The API's own id rather than the one asked for: they agree, and
                // the answer should say what it is describing.
                id = Field(fields, (int)Plugin.SubscriptionMetaDataType.Id, id),
                title = Field(fields, (int)Plugin.SubscriptionMetaDataType.Title),
                grouping = Field(fields, (int)Plugin.SubscriptionMetaDataType.Grouping),
                genre = Field(fields, (int)Plugin.SubscriptionMetaDataType.Genre),
                description = Field(fields, (int)Plugin.SubscriptionMetaDataType.Description),
                downloaded_count = Count(fields, (int)Plugin.SubscriptionMetaDataType.DounloadedCount),
                // The podcast API has no episode count; the URL enumeration is
                // one call and reads no metadata, so it answers for one.
                episode_count = EpisodeUrls(id).Length,
            };
        }

        private PodcastEpisode ReadEpisode(string id, int index, string[] urls)
        {
            if (index < 0 || index >= urls.Length)
                return null;
            if (!_api.Podcasts_GetSubscriptionEpisode(id, index, out var fields) || fields == null)
                return null;

            return new PodcastEpisode
            {
                index = index,
                id = Field(fields, (int)Plugin.EpisodeMetaDataType.Id),
                title = Field(fields, (int)Plugin.EpisodeMetaDataType.Title),
                date = Field(fields, (int)Plugin.EpisodeMetaDataType.DateTime).ToIso8601Utc(),
                description = Field(fields, (int)Plugin.EpisodeMetaDataType.Description),
                // The feed's own spelling; the core parses it to milliseconds.
                duration = Field(fields, (int)Plugin.EpisodeMetaDataType.Duration),
                is_downloaded = Flag(fields, (int)Plugin.EpisodeMetaDataType.IsDownloaded),
                has_been_played = Flag(fields, (int)Plugin.EpisodeMetaDataType.HasBeenPlayed),
                url = urls[index],
                author = Author(urls[index]),
            };
        }

        /// <summary>
        ///     Who made an episode, which the podcast API does not report.
        ///     MusicBee resolves a feed URL through the ordinary tag path, so the
        ///     episode's own artist tag answers it; empty when it cannot.
        /// </summary>
        private string Author(string url)
        {
            var artist = _api.Library_GetFileTag(url, Plugin.MetaDataType.Artist).Cleanup();
            return artist.Length > 0
                ? artist
                : _api.Library_GetFileTag(url, Plugin.MetaDataType.AlbumArtist).Cleanup();
        }

        /// <summary>
        ///     One metadata field. The API returns an array indexed by its own enum
        ///     and says nothing about its length, so a short array is a missing
        ///     field rather than an exception.
        /// </summary>
        private static string Field(string[] fields, int at, string fallback = "")
        {
            if (at < 0 || at >= fields.Length)
                return fallback;
            var value = fields[at].Cleanup();
            return value.Length > 0 ? value : fallback;
        }

        private static int Count(string[] fields, int at)
        {
            return int.TryParse(Field(fields, at), out var count) && count > 0 ? count : 0;
        }

        private static bool Flag(string[] fields, int at)
        {
            var raw = Field(fields, at);
            return bool.TryParse(raw, out var flag) ? flag : raw == "1";
        }
    }
}
