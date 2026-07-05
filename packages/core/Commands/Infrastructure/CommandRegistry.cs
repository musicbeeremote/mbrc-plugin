using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Commands.Handlers;
using MusicBeePlugin.Models.Requests;
using MusicBeePlugin.Protocol.Messages;

namespace MusicBeePlugin.Commands.Infrastructure
{
    /// <summary>
    ///     Simple replacement for configuration.cs - registers all commands with delegate dispatcher
    /// </summary>
    public static class CommandRegistry
    {
        /// <summary>
        ///     Register DI-enabled migrated player commands
        /// </summary>
        public static void RegisterPlayerCommands(ICommandRegistrar registrar,
            PlayerCommands playerCommands)
        {
            registrar.RegisterCommand(ProtocolConstants.PlayerPlay, playerCommands.HandlePlay);
            registrar.RegisterCommand(ProtocolConstants.PlayerPause, playerCommands.HandlePause);
            registrar.RegisterCommand(ProtocolConstants.PlayerPlayPause, playerCommands.HandlePlayPause);
            registrar.RegisterCommand(ProtocolConstants.PlayerStop, playerCommands.HandleStop);
            registrar.RegisterCommand(ProtocolConstants.PlayerNext, playerCommands.HandleNext);
            registrar.RegisterCommand(ProtocolConstants.PlayerPrevious, playerCommands.HandlePrevious);
            registrar.RegisterCommand(ProtocolConstants.PlayerVolume, playerCommands.HandleVolumeSet);
            registrar.RegisterCommand(ProtocolConstants.PlayerMute, playerCommands.HandleMuteSet);
            registrar.RegisterCommand(ProtocolConstants.PlayerShuffle, playerCommands.HandleShuffle);
            registrar.RegisterCommand(ProtocolConstants.PlayerScrobble, playerCommands.HandleScrobble);
            registrar.RegisterCommand(ProtocolConstants.PlayerAutoDj, playerCommands.HandleAutoDj);
            registrar.RegisterCommand(ProtocolConstants.PlayerRepeat, playerCommands.HandleRepeat);
            registrar.RegisterCommand(ProtocolConstants.PlayerStatus, playerCommands.HandlePlayerStatus);
            registrar.RegisterCommand(ProtocolConstants.PlayerOutput, playerCommands.HandleOutputDevices);
            registrar.RegisterCommand(ProtocolConstants.PlayerOutputSwitch, playerCommands.HandleOutputDeviceSwitch);
        }

        /// <summary>
        ///     Register DI-enabled migrated now playing commands
        /// </summary>
        public static void RegisterNowPlayingCommands(ICommandRegistrar registrar,
            NowPlayingCommands nowPlayingCommands)
        {
            registrar.RegisterCommand(ProtocolConstants.NowPlayingTrack, nowPlayingCommands.HandleTrackInfo);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingDetails, nowPlayingCommands.HandleTrackDetails);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingPosition, nowPlayingCommands.HandlePlaybackPosition);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingCover, nowPlayingCommands.HandleCover);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingLyrics, nowPlayingCommands.HandleLyrics);
            registrar.RegisterCommand<MoveTrackRequest>(ProtocolConstants.NowPlayingListMove, nowPlayingCommands.HandleMoveTrack);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingListRemove, nowPlayingCommands.HandleRemoveTrack);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingListSearch, nowPlayingCommands.HandleSearch);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingListPlay, nowPlayingCommands.HandleNowPlayingListPlay);
            registrar.RegisterCommand<QueueRequest>(ProtocolConstants.NowPlayingQueue, nowPlayingCommands.HandleQueue);
            registrar.RegisterCommand<PaginationRequest>(ProtocolConstants.NowPlayingList, nowPlayingCommands.HandleNowPlayingList);
            registrar.RegisterCommand<TagChangeRequest>(ProtocolConstants.NowPlayingTagChange, nowPlayingCommands.HandleTagChange);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingRating, nowPlayingCommands.HandleRating);
            registrar.RegisterCommand(ProtocolConstants.NowPlayingLfmRating, nowPlayingCommands.HandleLastfmLoveRating);
            registrar.RegisterCommand(ProtocolConstants.Init, nowPlayingCommands.HandleInit);
        }

        /// <summary>
        ///     Register playlist-related commands with dependency injection
        /// </summary>
        public static void RegisterPlaylistCommands(ICommandRegistrar registrar,
            PlaylistCommands playlistCommands)
        {
            registrar.RegisterCommand(ProtocolConstants.PlaylistPlay, playlistCommands.HandlePlaylistPlay);
            registrar.RegisterCommand<PaginationRequest>(ProtocolConstants.PlaylistList, playlistCommands.HandlePlaylistList);
        }

        /// <summary>
        ///     Register system-related commands with dependency injection
        /// </summary>
        public static void RegisterSystemCommands(ICommandRegistrar registrar,
            SystemCommands systemCommands)
        {
            registrar.RegisterCommand<ProtocolHandshakeRequest>(ProtocolConstants.Protocol, systemCommands.HandleProtocol);
            registrar.RegisterCommand(ProtocolConstants.Player, systemCommands.HandlePlayer);
            registrar.RegisterCommand(ProtocolConstants.PluginVersion, systemCommands.HandlePluginVersion);
            registrar.RegisterCommand(ProtocolConstants.Ping, systemCommands.HandlePing);
            registrar.RegisterCommand(ProtocolConstants.Pong, systemCommands.HandlePong);
        }

        /// <summary>
        ///     Register library-related commands with dependency injection
        /// </summary>
        public static void RegisterLibraryCommands(ICommandRegistrar registrar,
            LibraryCommands libraryCommands)
        {
            registrar.RegisterCommand<PaginationRequest>(ProtocolConstants.RadioStations, libraryCommands.HandleRadioStations);
            registrar.RegisterCommand(ProtocolConstants.LibrarySearchTitle, libraryCommands.HandleSearchTitle);
            registrar.RegisterCommand(ProtocolConstants.LibrarySearchGenre, libraryCommands.HandleSearchGenre);
            registrar.RegisterCommand(ProtocolConstants.LibrarySearchArtist, libraryCommands.HandleSearchArtist);
            registrar.RegisterCommand(ProtocolConstants.LibrarySearchAlbum, libraryCommands.HandleSearchAlbum);
            registrar.RegisterCommand<SearchRequest>(ProtocolConstants.LibraryQueueTrack, libraryCommands.HandleQueueTrack);
            registrar.RegisterCommand<SearchRequest>(ProtocolConstants.LibraryQueueGenre, libraryCommands.HandleQueueGenre);
            registrar.RegisterCommand<SearchRequest>(ProtocolConstants.LibraryQueueArtist, libraryCommands.HandleQueueArtist);
            registrar.RegisterCommand<SearchRequest>(ProtocolConstants.LibraryQueueAlbum, libraryCommands.HandleQueueAlbum);
            registrar.RegisterCommand<PaginationRequest>(ProtocolConstants.LibraryBrowseGenres, libraryCommands.HandleBrowseGenres);
            registrar.RegisterCommand<BrowseArtistsRequest>(ProtocolConstants.LibraryBrowseArtists, libraryCommands.HandleBrowseArtists);
            registrar.RegisterCommand<PaginationRequest>(ProtocolConstants.LibraryBrowseAlbums, libraryCommands.HandleBrowseAlbums);
            registrar.RegisterCommand<PaginationRequest>(ProtocolConstants.LibraryBrowseTracks, libraryCommands.HandleBrowseTracks);
            registrar.RegisterCommand(ProtocolConstants.LibraryPlayAll, libraryCommands.HandlePlayAll);
            registrar.RegisterCommand(ProtocolConstants.LibraryAlbumTracks, libraryCommands.HandleAlbumTracks);
            registrar.RegisterCommand(ProtocolConstants.LibraryArtistAlbums, libraryCommands.HandleArtistAlbums);
            registrar.RegisterCommand(ProtocolConstants.LibraryGenreArtists, libraryCommands.HandleGenreArtists);
            registrar.RegisterCommand<AlbumCoverRequest>(ProtocolConstants.LibraryAlbumCover, libraryCommands.HandleAlbumCover);
            registrar.RegisterCommand(ProtocolConstants.LibraryCoverCacheBuildStatus, libraryCommands.HandleCoverCacheStatus);
        }
    }
}
