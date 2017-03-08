using TinyIoC;

namespace MusicBeePlugin.AndroidRemote
{
    using Commands.InstaReplies;
    using Commands.Requests;
    using Networking;

    internal class Configuration
    {
        public static void Register(Controller.Controller controller, TinyIoCContainer container)
        {
            /** Protocol Related commands **/
            controller.AddCommand(Constants.Player, container.Resolve<RequestPlayer>());
            controller.AddCommand(Constants.Protocol, container.Resolve<RequestProtocol>());            
            controller.AddCommand(Constants.PluginVersion, container.Resolve<RequestPluginVersion>());
            controller.AddCommand(Constants.PlaylistList, container.Resolve<RequestPlaylistList>());
            controller.AddCommand(Constants.PlayerNext, container.Resolve<RequestNextTrack>());
            controller.AddCommand(Constants.PlayerPlayPause, container.Resolve<RequestPlayPause>());
            controller.AddCommand(Constants.PlayerPrevious, container.Resolve<RequestPreviousTrack>());
            controller.AddCommand(Constants.PlayerStop, container.Resolve<RequestStop>());
            controller.AddCommand(Constants.PlayerVolume, container.Resolve<RequestVolume>());
            controller.AddCommand(Constants.PlayerStatus, container.Resolve<RequestPlayerStatus>());
            controller.AddCommand(Constants.PlayerAutoDj, container.Resolve<RequestAutoDj>());
            controller.AddCommand(Constants.PlayerShuffle, container.Resolve<RequestShuffle>());
            controller.AddCommand(Constants.PlayerScrobble, container.Resolve<RequestScrobble>());
            controller.AddCommand(Constants.PlayerRepeat, container.Resolve<RequestRepeat>());
            controller.AddCommand(Constants.PlayerMute, container.Resolve<RequestMute>());
            controller.AddCommand(Constants.NowPlayingPosition, container.Resolve<RequestPlaybackPosition>());
            controller.AddCommand(Constants.NowPlayingListRemove, container.Resolve<RequestNowPlayingTrackRemoval>());
            controller.AddCommand(Constants.NowPlayingListPlay, container.Resolve<RequestNowPlayingPlay>());
            controller.AddCommand(Constants.NowPlayingList, container.Resolve<RequestNowPlayingList>());
            controller.AddCommand(Constants.NowPlayingLfmRating, container.Resolve<RequestLfmLoveRating>());
            controller.AddCommand(Constants.NowPlayingTrack, container.Resolve<RequestSongInfo>());
            controller.AddCommand(Constants.NowPlayingCover, container.Resolve<RequestCover>());
            controller.AddCommand(Constants.NowPlayingLyrics, container.Resolve<RequestLyrics>());
            controller.AddCommand(Constants.NowPlayingRating, container.Resolve<RequestRating>());
            controller.AddCommand(Constants.NowPlayingListSearch, container.Resolve<RequestNowPlayingSearch>());
            controller.AddCommand(Constants.NowPlayingListMove, container.Resolve<RequestNowPlayingMoveTrack>());
            controller.AddCommand(Constants.LibrarySearchArtist, container.Resolve<RequestLibSearchArtist>());
            controller.AddCommand(Constants.LibrarySearchAlbum, container.Resolve<RequestLibSearchAlbum>());
            controller.AddCommand(Constants.LibrarySearchGenre, container.Resolve<RequestLibSearchGenre>());
            controller.AddCommand(Constants.LibrarySearchTitle, container.Resolve<RequestLibSearchTitle>());
            controller.AddCommand(Constants.LibraryQueueAlbum, container.Resolve<RequestLibQueueAlbum>());
            controller.AddCommand(Constants.LibraryQueueArtist, container.Resolve<RequestLibQueueArtist>());
            controller.AddCommand(Constants.LibraryQueueGenre, container.Resolve<RequestLibQueueGenre>());
            controller.AddCommand(Constants.LibraryQueueTrack, container.Resolve<RequestLibQueueTrack>());
            controller.AddCommand(Constants.LibraryArtistAlbums, container.Resolve<RequestLibArtistAlbums>());
            controller.AddCommand(Constants.LibraryAlbumTracks, container.Resolve<RequestLibAlbumTracks>());
            controller.AddCommand(Constants.LibraryGenreArtists, container.Resolve<RequestLibGenreArtists>());

            #region Protocol 2.1
            controller.AddCommand(Constants.Pong, container.Resolve<HandlePong>());
            controller.AddCommand(Constants.Ping, container.Resolve<PingReply>());
            controller.AddCommand(Constants.Init, container.Resolve<ProcessInitRequest>());
            controller.AddCommand(Constants.PlayerPlay, container.Resolve<RequestPlay>());
            controller.AddCommand(Constants.PlayerPause, container.Resolve<RequestPause>());

            #endregion

            #region Protocol 3
            controller.AddCommand(Constants.PlaylistPlay, container.Resolve<RequestPlaylistPlay>());
            controller.AddCommand(Constants.LibraryBrowseGenres, container.Resolve<RequestBrowseGenres>());
            controller.AddCommand(Constants.LibraryBrowseArtists, container.Resolve<RequestBrowseArtists>());
            controller.AddCommand(Constants.LibraryBrowseAlbums, container.Resolve<RequestBrowseAlbums>());
            controller.AddCommand(Constants.LibraryBrowseTracks, container.Resolve<RequestBrowseTracks>());
            controller.AddCommand(Constants.NowPlayingQueue, container.Resolve<RequestNowplayingQueue>());
            #endregion

            #region Protocol 4
            controller.AddCommand(Constants.PlayerOutput, container.Resolve<RequestOutputDeviceList>());
            controller.AddCommand(Constants.PlayerOutputSwitch, container.Resolve<RequestPlayerOutputSwitch>());
            controller.AddCommand(Constants.RadioStations, container.Resolve<RequestRadioStations>());
            #endregion

        }
    }
}