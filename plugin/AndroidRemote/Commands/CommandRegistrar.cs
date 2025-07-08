using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands
{
    /// <summary>
    /// Handles registration of all commands with dependency injection
    /// </summary>
    internal class CommandRegistrar
    {
        private readonly CommandFactory _commandFactory;
        private readonly IServiceLocator _serviceLocator;

        public CommandRegistrar(CommandFactory commandFactory, IServiceLocator serviceLocator)
        {
            _commandFactory = commandFactory;
            _serviceLocator = serviceLocator;
        }

        /// <summary>
        /// Registers all refactored commands with their dependencies
        /// </summary>
        public void RegisterAllCommands()
        {
            RegisterPlayerControlCommands();
            RegisterStateManagementCommands();
            RegisterNowPlayingCommands();
            RegisterLibraryCommands();
            RegisterPlaylistCommands();
            RegisterProtocolCommands();
            RegisterInstaRepliesCommands();
            RegisterInternalCommands();
            RegisterStateCommands();
            RegisterOtherCommands();
        }

        /// <summary>
        /// Registers player control commands
        /// </summary>
        private void RegisterPlayerControlCommands()
        {
            _commandFactory.RegisterCommand<Requests.RequestPlay>(() => 
                new Requests.RequestPlay(_serviceLocator.GetService<IPlayerService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestPause>(() => 
                new Requests.RequestPause(_serviceLocator.GetService<IPlayerService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestStop>(() => 
                new Requests.RequestStop(_serviceLocator.GetService<IPlayerService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestNextTrack>(() => 
                new Requests.RequestNextTrack(_serviceLocator.GetService<IPlayerService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestPreviousTrack>(() => 
                new Requests.RequestPreviousTrack(_serviceLocator.GetService<IPlayerService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestPlayPause>(() => 
                new Requests.RequestPlayPause(_serviceLocator.GetService<IPlayerService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestPlayerStatus>(() => 
                new Requests.RequestPlayerStatus(_serviceLocator.GetService<IPlayerService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestPlaybackPosition>(() => 
                new Requests.RequestPlaybackPosition(_serviceLocator.GetService<IPlayerService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestVolume>(() => 
                new Requests.RequestVolume(_serviceLocator.GetService<IPlayerService>()));
        }

        /// <summary>
        /// Registers state management commands (shuffle, repeat, mute, etc.)
        /// </summary>
        private void RegisterStateManagementCommands()
        {
            _commandFactory.RegisterCommand<Requests.RequestMute>(() => 
                new Requests.RequestMute(_serviceLocator.GetService<IPlayerService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestShuffle>(() => 
                new Requests.RequestShuffle(_serviceLocator.GetService<IPlayerService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestRepeat>(() => 
                new Requests.RequestRepeat(_serviceLocator.GetService<IPlayerService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestScrobble>(() => 
                new Requests.RequestScrobble(_serviceLocator.GetService<IPlayerService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestAutoDj>(() => 
                new Requests.RequestAutoDj(_serviceLocator.GetService<IPlayerService>()));
        }

        /// <summary>
        /// Registers now playing commands
        /// </summary>
        private void RegisterNowPlayingCommands()
        {
            _commandFactory.RegisterCommand<Requests.RequestSongInfo>(() => 
                new Requests.RequestSongInfo(_serviceLocator.GetService<INowPlayingService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestSongDetails>(() => 
                new Requests.RequestSongDetails(_serviceLocator.GetService<INowPlayingService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestRating>(() => 
                new Requests.RequestRating(_serviceLocator.GetService<INowPlayingService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestNowPlayingList>(() => 
                new Requests.RequestNowPlayingList(_serviceLocator.GetService<INowPlayingService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestNowPlayingMoveTrack>(() => 
                new Requests.RequestNowPlayingMoveTrack(_serviceLocator.GetService<INowPlayingService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestNowPlayingPlay>(() => 
                new Requests.RequestNowPlayingPlay(_serviceLocator.GetService<INowPlayingService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestNowPlayingQueue>(() => 
                new Requests.RequestNowPlayingQueue(_serviceLocator.GetService<INowPlayingService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestNowPlayingSearch>(() => 
                new Requests.RequestNowPlayingSearch(_serviceLocator.GetService<INowPlayingService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestNowPlayingTrackRemoval>(() => 
                new Requests.RequestNowPlayingTrackRemoval(_serviceLocator.GetService<INowPlayingService>()));
        }

        /// <summary>
        /// Registers library browsing and search commands
        /// </summary>
        private void RegisterLibraryCommands()
        {
            _commandFactory.RegisterCommand<Requests.RequestBrowseAlbums>(() => 
                new Requests.RequestBrowseAlbums(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestBrowseArtists>(() => 
                new Requests.RequestBrowseArtists(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestBrowseTracks>(() => 
                new Requests.RequestBrowseTracks(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestBrowseGenres>(() => 
                new Requests.RequestBrowseGenres(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestOutputDeviceList>(() => 
                new Requests.RequestOutputDeviceList(_serviceLocator.GetService<IPlayerService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestPlayerOutputSwitch>(() => 
                new Requests.RequestPlayerOutputSwitch(_serviceLocator.GetService<IPlayerService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestLibSearchTitle>(() => 
                new Requests.RequestLibSearchTitle(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestLibSearchArtist>(() => 
                new Requests.RequestLibSearchArtist(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestLibSearchAlbum>(() => 
                new Requests.RequestLibSearchAlbum(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestLibSearchGenre>(() => 
                new Requests.RequestLibSearchGenre(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestLibGenreArtists>(() => 
                new Requests.RequestLibGenreArtists(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestLibArtistAlbums>(() => 
                new Requests.RequestLibArtistAlbums(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestLibAlbumTracks>(() => 
                new Requests.RequestLibAlbumTracks(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestLibQueueTrack>(() => 
                new Requests.RequestLibQueueTrack(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestLibQueueGenre>(() => 
                new Requests.RequestLibQueueGenre(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestLibQueueArtist>(() => 
                new Requests.RequestLibQueueArtist(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestLibQueueAlbum>(() => 
                new Requests.RequestLibQueueAlbum(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestLibPlayAll>(() => 
                new Requests.RequestLibPlayAll(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestLibraryAlbumCover>(() => 
                new Requests.RequestLibraryAlbumCover(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestRadioStations>(() => 
                new Requests.RequestRadioStations(_serviceLocator.GetService<ILibraryService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestTagChange>(() => 
                new Requests.RequestTagChange(_serviceLocator.GetService<IPlayerService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestLfmLoveRating>(() => 
                new Requests.RequestLfmLoveRating(_serviceLocator.GetService<IPlayerService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestCoverCacheBuildStatus>(() => 
                new Requests.RequestCoverCacheBuildStatus(_serviceLocator.GetService<ISettingsService>()));
            
            _commandFactory.RegisterCommand<Internal.SocketStatusChanged>(() => 
                new Internal.SocketStatusChanged(_serviceLocator.GetService<ISettingsService>()));
            
            _commandFactory.RegisterCommand<Internal.ShowFirstRunDialogCommand>(() => 
                new Internal.ShowFirstRunDialogCommand(_serviceLocator.GetService<ISettingsService>()));
            
            _commandFactory.RegisterCommand<Internal.InitializeModelStateCommand>(() => 
                new Internal.InitializeModelStateCommand(_serviceLocator.GetService<INowPlayingService>()));
            
            _commandFactory.RegisterCommand<InstaReplies.RequestLyrics>(() => 
                new InstaReplies.RequestLyrics(_serviceLocator.GetService<INowPlayingService>()));
            
            _commandFactory.RegisterCommand<InstaReplies.ProcessInitRequest>(() => 
                new InstaReplies.ProcessInitRequest(_serviceLocator.GetService<IPlayerService>(), _serviceLocator.GetService<INowPlayingService>()));
        }

        /// <summary>
        /// Registers playlist commands
        /// </summary>
        private void RegisterPlaylistCommands()
        {
            _commandFactory.RegisterCommand<Requests.RequestPlaylistList>(() => 
                new Requests.RequestPlaylistList(_serviceLocator.GetService<IPlaylistService>()));
            
            _commandFactory.RegisterCommand<Requests.RequestPlaylistPlay>(() => 
                new Requests.RequestPlaylistPlay(_serviceLocator.GetService<IPlaylistService>()));
        }

        /// <summary>
        /// Registers protocol negotiation commands
        /// </summary>
        private void RegisterProtocolCommands()
        {
            _commandFactory.RegisterCommand<Requests.RequestPlayer>(() => 
                new Requests.RequestPlayer());
            
            _commandFactory.RegisterCommand<Requests.RequestProtocol>(() => 
                new Requests.RequestProtocol());
            
            _commandFactory.RegisterCommand<Requests.RequestPluginVersion>(() => 
                new Requests.RequestPluginVersion());
        }

        /// <summary>
        /// Registers instant reply commands
        /// </summary>
        private void RegisterInstaRepliesCommands()
        {
            _commandFactory.RegisterCommand<InstaReplies.HandlePong>(() => 
                new InstaReplies.HandlePong());
            
            _commandFactory.RegisterCommand<InstaReplies.PingReply>(() => 
                new InstaReplies.PingReply());
            
            _commandFactory.RegisterCommand<InstaReplies.RequestCover>(() => 
                new InstaReplies.RequestCover());
        }

        /// <summary>
        /// Registers internal system commands
        /// </summary>
        private void RegisterInternalCommands()
        {
            _commandFactory.RegisterCommand<Internal.BroadcastEventAvailable>(() => 
                new Internal.BroadcastEventAvailable());
            
            _commandFactory.RegisterCommand<Internal.ClientConnected>(() => 
                new Internal.ClientConnected());
            
            _commandFactory.RegisterCommand<Internal.ClientDisconnected>(() => 
                new Internal.ClientDisconnected());
            
            _commandFactory.RegisterCommand<Internal.ForceClientDisconnect>(() => 
                new Internal.ForceClientDisconnect());
            
            _commandFactory.RegisterCommand<Internal.RestartSocketCommand>(() => 
                new Internal.RestartSocketCommand());
            
            _commandFactory.RegisterCommand<Internal.StartServiceBroadcast>(() => 
                new Internal.StartServiceBroadcast());
            
            _commandFactory.RegisterCommand<Internal.StartSocketServer>(() => 
                new Internal.StartSocketServer());
            
            _commandFactory.RegisterCommand<Internal.StopSocketServer>(() => 
                new Internal.StopSocketServer());
        }

        /// <summary>
        /// Registers state change commands
        /// </summary>
        private void RegisterStateCommands()
        {
            _commandFactory.RegisterCommand<State.PCoverChanged>(() => 
                new State.PCoverChanged());
            
            _commandFactory.RegisterCommand<State.PLyricsChanged>(() => 
                new State.PLyricsChanged());
        }

        /// <summary>
        /// Registers other miscellaneous commands
        /// </summary>
        private void RegisterOtherCommands()
        {
            _commandFactory.RegisterCommand<ReplayAvailable>(() => 
                new ReplayAvailable());
        }

        /// <summary>
        /// Helper method to register a command with a single service dependency
        /// </summary>
        /// <typeparam name="TCommand">Command type</typeparam>
        /// <typeparam name="TService">Service type</typeparam>
        /// <remarks>
        /// This method can be used for commands that take a single service dependency
        /// in their constructor. For more complex commands, use the direct registration approach.
        /// </remarks>
        private void RegisterCommand<TCommand, TService>() 
            where TCommand : class, new()
            where TService : class
        {
            // This is a placeholder for future implementation
            // Would need reflection or other mechanisms to work generically
            // For now, explicit registration is clearer and more maintainable
        }
    }
}