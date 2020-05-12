using System;
using System.Globalization;

namespace MusicBeeRemote.Core.Podcasts
{
    public static class SubscriptionConverter
    {
        public static PodcastSubscription Convert(string[] metadata)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            return new PodcastSubscription
            {
                Id = metadata[0],
                Title = metadata[1],
                Grouping = metadata[2],
                Genre = metadata[3],
                Description = metadata[4],
                Downloaded = uint.Parse(metadata[5], CultureInfo.CurrentCulture),
            };
        }
    }
}
