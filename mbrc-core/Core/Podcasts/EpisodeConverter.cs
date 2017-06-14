using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MusicBeeRemote.Core.Podcasts
{
    public class EpisodeConverter
    {
        public PodcastEpisode Convert(string[] metadata)
        {
            return new PodcastEpisode
            {
                Id = metadata[0],
                Title = metadata[1],
                Date = DateTime.Parse(metadata[2]),
                Description = metadata[3],
                Duration = metadata[4],
                Downloaded = bool.Parse(metadata[5]),
                Played = bool.Parse(metadata[6])
            };
        }
    }
}