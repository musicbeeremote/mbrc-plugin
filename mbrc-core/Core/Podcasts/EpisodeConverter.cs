using System;
using System.Globalization;

namespace MusicBeeRemote.Core.Podcasts
{
    public static class EpisodeConverter
    {
        public static PodcastEpisode Convert(string[] metadata)
        {
            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            return new PodcastEpisode
            {
                Id = metadata[0],
                Title = metadata[1],
                Date = DateTime.Parse(metadata[2], CultureInfo.CurrentCulture),
                Description = metadata[3],
                Duration = metadata[4],
                Downloaded = bool.Parse(metadata[5]),
                Played = bool.Parse(metadata[6]),
            };
        }
    }
}
