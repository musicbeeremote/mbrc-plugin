namespace MusicBeeRemote.Core.Podcasts
{
    public class SubscriptionConverter
    {
        public PodcastSubscription Convert(string[] metadata)
        {
            return new PodcastSubscription
            {
                Id = metadata[0],
                Title = metadata[1],
                Grouping = metadata[2],
                Genre = metadata[3],
                Description = metadata[4],
                Downloaded = uint.Parse(metadata[5])
            };
        }
    }
}
