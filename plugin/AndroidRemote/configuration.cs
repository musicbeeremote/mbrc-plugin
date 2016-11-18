namespace MusicBeePlugin.AndroidRemote
{
    using Commands;
    using Commands.InstaReplies;
    using Commands.Internal;
    using Commands.Requests;
    using Commands.State;
    using Networking;
    using Events;

    internal class Configuration
    {
        public static void Register(Controller.Controller controller)
        {
            controller.AddCommand(EventType.ActionSocketStart, typeof (StartSocketServer));
            controller.AddCommand(EventType.ActionSocketStop, typeof (StopSocketServer));
            controller.AddCommand(EventType.ActionClientConnected, typeof (ClientConnected));
            controller.AddCommand(EventType.ActionClientDisconnected, typeof (ClientDisconnected));
            controller.AddCommand(EventType.ActionForceClientDisconnect, typeof (ForceClientDisconnect));
            controller.AddCommand(EventType.InitializeModel, typeof(InitializeModelStateCommand));
            controller.AddCommand(EventType.NowPlayingCoverChange, typeof(PCoverChanged));
            controller.AddCommand(EventType.NowPlayingLyricsChange, typeof(PLyricsChanged));
            controller.AddCommand(EventType.StartServiceBroadcast, typeof(StartServiceBroadcast));
            controller.AddCommand(EventType.SocketStatusChange, typeof(SocketStatusChanged));
            controller.AddCommand(EventType.RestartSocket, typeof(RestartSocketCommand));
            controller.AddCommand(EventType.ShowFirstRunDialog, typeof(ShowFirstRunDialogCommand));
            /** Protocol Related commands **/
            controller.AddCommand(Constants.Player, typeof (RequestPlayer));
            controller.AddCommand(Constants.Protocol, typeof (RequestProtocol));            
            controller.AddCommand(Constants.PluginVersion, typeof (RequestPluginVersion));
            controller.AddCommand(Constants.PlaylistList, typeof (RequestPlaylistList));
            controller.AddCommand(Constants.PlayerNext, typeof(RequestNextTrack));
            controller.AddCommand(Constants.PlayerPlayPause, typeof(RequestPlayPause));
            controller.AddCommand(Constants.PlayerPrevious, typeof(RequestPreviousTrack));
            controller.AddCommand(Constants.PlayerStop, typeof(RequestStop));
            controller.AddCommand(Constants.PlayerVolume, typeof(RequestVolume));
            controller.AddCommand(Constants.PlayerStatus, typeof(RequestPlayerStatus));
            controller.AddCommand(Constants.PlayerAutoDj, typeof(RequestAutoDj));
            controller.AddCommand(Constants.PlayerShuffle, typeof(RequestShuffle));
            controller.AddCommand(Constants.PlayerScrobble, typeof(RequestScrobble));
            controller.AddCommand(Constants.PlayerRepeat, typeof(RequestRepeat));
            controller.AddCommand(Constants.PlayerMute, typeof(RequestMute));
            controller.AddCommand(Constants.NowPlayingPosition, typeof(RequestPlaybackPosition));
            controller.AddCommand(Constants.NowPlayingListRemove, typeof(RequestNowPlayingTrackRemoval));
            controller.AddCommand(Constants.NowPlayingListPlay, typeof(RequestNowPlayingPlay));
            controller.AddCommand(Constants.NowPlayingList, typeof(RequestNowPlayingList));
            controller.AddCommand(Constants.NowPlayingLfmRating, typeof(RequestLfmLoveRating));
            controller.AddCommand(Constants.NowPlayingTrack, typeof(RequestSongInfo));
            controller.AddCommand(Constants.NowPlayingCover, typeof(RequestCover));
            controller.AddCommand(Constants.NowPlayingLyrics, typeof(RequestLyrics));
            controller.AddCommand(Constants.NowPlayingRating, typeof(RequestRating));
            controller.AddCommand(Constants.NowPlayingListSearch, typeof(RequestNowPlayingSearch));
            controller.AddCommand(Constants.NowPlayingListMove, typeof(RequestNowPlayingMoveTrack));
            controller.AddCommand(Constants.LibrarySearchArtist, typeof(RequestLibSearchArtist));
            controller.AddCommand(Constants.LibrarySearchAlbum, typeof(RequestLibSearchAlbum));
            controller.AddCommand(Constants.LibrarySearchGenre, typeof(RequestLibSearchGenre));
            controller.AddCommand(Constants.LibrarySearchTitle, typeof(RequestLibSearchTitle));
            controller.AddCommand(Constants.LibraryQueueAlbum, typeof(RequestLibQueueAlbum));
            controller.AddCommand(Constants.LibraryQueueArtist, typeof(RequestLibQueueArtist));
            controller.AddCommand(Constants.LibraryQueueGenre, typeof(RequestLibQueueGenre));
            controller.AddCommand(Constants.LibraryQueueTrack, typeof(RequestLibQueueTrack));
            controller.AddCommand(EventType.ReplyAvailable, typeof(ReplayAvailable));
            controller.AddCommand(Constants.LibraryArtistAlbums, typeof(RequestLibArtistAlbums));
            controller.AddCommand(Constants.LibraryAlbumTracks, typeof(RequestLibAlbumTracks));
            controller.AddCommand(Constants.LibraryGenreArtists, typeof(RequestLibGenreArtists));

            #region Protocol 2.1
            controller.AddCommand(Constants.Pong, typeof(HandlePong));
            controller.AddCommand(Constants.Ping, typeof(PingReply));
            controller.AddCommand(Constants.Init, typeof(ProcessInitRequest));
            controller.AddCommand(Constants.PlayerPlay, typeof(RequestPlay));
            controller.AddCommand(Constants.PlayerPause, typeof (RequestPause));

            #endregion

            #region Protocol 3
            controller.AddCommand(Constants.PlaylistPlay, typeof(RequestPlaylistPlay));
            controller.AddCommand(Constants.LibraryBrowseGenres, typeof(RequestBrowseGenres));
            controller.AddCommand(Constants.LibraryBrowseArtists, typeof(RequestBrowseArtists));
            controller.AddCommand(Constants.LibraryBrowseAlbums, typeof(RequestBrowseAlbums));
            controller.AddCommand(Constants.LibraryBrowseTracks, typeof(RequestBrowseTracks));
            controller.AddCommand(EventType.BroadcastEvent, typeof(BroadcastEventAvailable));
            controller.AddCommand(Constants.NowPlayingQueue, typeof(RequestNowplayingQueue));
            #endregion

        }
    }
}