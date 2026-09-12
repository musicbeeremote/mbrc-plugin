using System.Collections.Generic;
using MusicBeePlugin.Ffi;

namespace MusicBeePlugin.Providers
{
    /// <summary>
    ///     Podcast subscriptions and their episodes.
    /// </summary>
    public interface IPodcastDataProvider
    {
        /// <summary>
        ///     A page of subscriptions. The ids are enumerated in one call and
        ///     metadata is read for the window alone, so a long list costs a page.
        /// </summary>
        Page<PodcastSubscription> GetSubscriptions(int offset, int limit);

        /// <summary>
        ///     One subscription, or an empty list when no subscription has that
        ///     id. A list rather than null because that is how the core spells
        ///     "not there" across the boundary.
        /// </summary>
        List<PodcastSubscription> GetSubscription(string id);

        /// <summary>
        ///     A page of one subscription's episodes, newest first as MusicBee
        ///     orders them. Metadata is read for the window alone.
        /// </summary>
        Page<PodcastEpisode> GetEpisodes(string id, int offset, int limit);

        /// <summary>
        ///     One episode by its place in the subscription, or an empty list when
        ///     the id is unknown or the index is past the end.
        /// </summary>
        List<PodcastEpisode> GetEpisode(string id, int index);

        /// <summary>
        ///     A subscription's artwork as base64, empty when it has none.
        /// </summary>
        string GetSubscriptionArtwork(string id);
    }
}
