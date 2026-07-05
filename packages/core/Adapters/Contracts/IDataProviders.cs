namespace MusicBeePlugin.Adapters.Contracts
{
    /// <summary>
    ///     Composite interface that provides access to all data providers.
    ///     This follows the same pattern as IMusicBeeApiAdapter for adapters.
    /// </summary>
    public interface IDataProviders
    {
        /// <summary>
        ///     Gets the player data provider.
        /// </summary>
        IPlayerDataProvider Player { get; }

        /// <summary>
        ///     Gets the track data provider.
        /// </summary>
        ITrackDataProvider Track { get; }

        /// <summary>
        ///     Gets the playlist data provider.
        /// </summary>
        IPlaylistDataProvider Playlist { get; }

        /// <summary>
        ///     Gets the library data provider.
        /// </summary>
        ILibraryDataProvider Library { get; }
    }
}
