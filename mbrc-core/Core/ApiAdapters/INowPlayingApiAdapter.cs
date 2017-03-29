namespace MusicBeeRemoteCore.Core.ApiAdapters
{
    public interface INowPlayingApiAdapter
    {
        /// <summary>
        /// Searchs the available metadata in the now playing list and plays the first track matching the
        /// query supplied.
        /// </summary>
        /// <param name="query">A string that will be used to filter the available tracks</param>
        /// <returns>True if it manaegd to play a track or false if it failed</returns>
        bool PlayMatchingTrack(string query);
    }
}