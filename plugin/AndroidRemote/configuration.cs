using StructureMap;

namespace MusicBeePlugin.AndroidRemote
{
    using Commands.InstaReplies;
    using Commands.Requests;
    using Networking;

    internal class Configuration
    {
        public static void Register(Controller.Controller controller, Container container)
        {
            /** Protocol Related commands **/
            controller.AddCommand(Constants.Player, container.GetInstance<RequestPlayer>());
            controller.AddCommand(Constants.Protocol, container.GetInstance<RequestProtocol>());            
            controller.AddCommand(Constants.PluginVersion, container.GetInstance<RequestPluginVersion>());
            controller.AddCommand(Constants.PlaylistList, container.GetInstance<RequestPlaylistList>());
            controller.AddCommand(Constants.PlayerNext, container.GetInstance<RequestNextTrack>());
            controller.AddCommand(Constants.PlayerPlayPause, container.GetInstance<RequestPlayPause>());
            controller.AddCommand(Constants.PlayerPrevious, container.GetInstance<RequestPreviousTrack>());
            controller.AddCommand(Constants.PlayerStop, container.GetInstance<RequestStop>());
            controller.AddCommand(Constants.PlayerVolume, container.GetInstance<RequestVolume>());
            controller.AddCommand(Constants.PlayerStatus, container.GetInstance<RequestPlayerStatus>());
            controller.AddCommand(Constants.PlayerAutoDj, container.GetInstance<RequestAutoDj>());
            controller.AddCommand(Constants.PlayerShuffle, container.GetInstance<RequestShuffle>());
            controller.AddCommand(Constants.PlayerScrobble, container.GetInstance<RequestScrobble>());
            controller.AddCommand(Constants.PlayerRepeat, container.GetInstance<RequestRepeat>());
            controller.AddCommand(Constants.PlayerMute, container.GetInstance<RequestMute>());
            controller.AddCommand(Constants.NowPlayingPosition, container.GetInstance<RequestPlaybackPosition>());
            controller.AddCommand(Constants.NowPlayingListRemove, container.GetInstance<RequestNowPlayingTrackRemoval>());
            controller.AddCommand(Constants.NowPlayingListPlay, container.GetInstance<RequestNowPlayingPlay>());
            controller.AddCommand(Constants.NowPlayingList, container.GetInstance<RequestNowPlayingList>());
            controller.AddCommand(Constants.NowPlayingLfmRating, container.GetInstance<RequestLfmLoveRating>());
            controller.AddCommand(Constants.NowPlayingTrack, container.GetInstance<RequestSongInfo>());
            controller.AddCommand(Constants.NowPlayingCover, container.GetInstance<RequestCover>());
            controller.AddCommand(Constants.NowPlayingLyrics, container.GetInstance<RequestLyrics>());
            controller.AddCommand(Constants.NowPlayingRating, container.GetInstance<RequestRating>());
            controller.AddCommand(Constants.NowPlayingListSearch, container.GetInstance<RequestNowPlayingSearch>());
            controller.AddCommand(Constants.NowPlayingListMove, container.GetInstance<RequestNowPlayingMoveTrack>());
            controller.AddCommand(Constants.LibrarySearchArtist, container.GetInstance<RequestLibSearchArtist>());
            controller.AddCommand(Constants.LibrarySearchAlbum, container.GetInstance<RequestLibSearchAlbum>());
            controller.AddCommand(Constants.LibrarySearchGenre, container.GetInstance<RequestLibSearchGenre>());
            controller.AddCommand(Constants.LibrarySearchTitle, container.GetInstance<RequestLibSearchTitle>());
            controller.AddCommand(Constants.LibraryQueueAlbum, container.GetInstance<RequestLibQueueAlbum>());
            controller.AddCommand(Constants.LibraryQueueArtist, container.GetInstance<RequestLibQueueArtist>());
            controller.AddCommand(Constants.LibraryQueueGenre, container.GetInstance<RequestLibQueueGenre>());
            controller.AddCommand(Constants.LibraryQueueTrack, container.GetInstance<RequestLibQueueTrack>());
            controller.AddCommand(Constants.LibraryArtistAlbums, container.GetInstance<RequestLibArtistAlbums>());
            controller.AddCommand(Constants.LibraryAlbumTracks, container.GetInstance<RequestLibAlbumTracks>());
            controller.AddCommand(Constants.LibraryGenreArtists, container.GetInstance<RequestLibGenreArtists>());

            #region Protocol 2.1
            controller.AddCommand(Constants.Pong, container.GetInstance<HandlePong>());
            controller.AddCommand(Constants.Ping, container.GetInstance<PingReply>());
            controller.AddCommand(Constants.Init, container.GetInstance<ProcessInitRequest>());
            controller.AddCommand(Constants.PlayerPlay, container.GetInstance<RequestPlay>());
            controller.AddCommand(Constants.PlayerPause, container.GetInstance<RequestPause>());

            #endregion

            #region Protocol 3
            controller.AddCommand(Constants.PlaylistPlay, container.GetInstance<RequestPlaylistPlay>());
            controller.AddCommand(Constants.LibraryBrowseGenres, container.GetInstance<RequestBrowseGenres>());
            controller.AddCommand(Constants.LibraryBrowseArtists, container.GetInstance<RequestBrowseArtists>());
            controller.AddCommand(Constants.LibraryBrowseAlbums, container.GetInstance<RequestBrowseAlbums>());
            controller.AddCommand(Constants.LibraryBrowseTracks, container.GetInstance<RequestBrowseTracks>());
            controller.AddCommand(Constants.NowPlayingQueue, container.GetInstance<RequestNowplayingQueue>());
            #endregion

            #region Protocol 4
            controller.AddCommand(Constants.PlayerOutput, container.GetInstance<RequestOutputDeviceList>());
            controller.AddCommand(Constants.PlayerOutputSwitch, container.GetInstance<RequestPlayerOutputSwitch>());
            controller.AddCommand(Constants.RadioStations, container.GetInstance<RequestRadioStations>());
            #endregion

        }
    }
}