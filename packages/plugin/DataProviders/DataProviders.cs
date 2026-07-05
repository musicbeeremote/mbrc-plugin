using MusicBeePlugin.Adapters.Contracts;

namespace MusicBeePlugin.DataProviders
{
    /// <summary>
    ///     Composite class that provides access to all data providers.
    ///     This follows the same pattern as MusicBeeApiAdapter for adapters.
    /// </summary>
    public class DataProviders : IDataProviders
    {
        public DataProviders(
            IPlayerDataProvider player,
            ITrackDataProvider track,
            IPlaylistDataProvider playlist,
            ILibraryDataProvider library)
        {
            Player = player;
            Track = track;
            Playlist = playlist;
            Library = library;
        }

        public IPlayerDataProvider Player { get; }
        public ITrackDataProvider Track { get; }
        public IPlaylistDataProvider Playlist { get; }
        public ILibraryDataProvider Library { get; }
    }
}
