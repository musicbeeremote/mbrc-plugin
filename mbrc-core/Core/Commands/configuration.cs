using MusicBeeRemote.Core.Commands.InstaReplies;
using MusicBeeRemote.Core.Commands.Requests;
using MusicBeeRemote.Core.Network;
using StructureMap;

namespace MusicBeeRemote.Core.Commands
{
    internal class Configuration
    {
        public static void Register(CommandExecutor commandExecutor, Container container)
        {
            /** Protocol Related commands **/
            commandExecutor.AddCommand(Constants.Player, container.GetInstance<RequestPlayer>());
            commandExecutor.AddCommand(Constants.Protocol, container.GetInstance<RequestProtocol>());            
            commandExecutor.AddCommand(Constants.PluginVersion, container.GetInstance<RequestPluginVersion>());
            commandExecutor.AddCommand(Constants.PlaylistList, container.GetInstance<RequestPlaylistList>());
            commandExecutor.AddCommand(Constants.PlayerNext, container.GetInstance<RequestNextTrack>());
            commandExecutor.AddCommand(Constants.PlayerPlayPause, container.GetInstance<RequestPlayPause>());
            commandExecutor.AddCommand(Constants.PlayerPrevious, container.GetInstance<RequestPreviousTrack>());
            commandExecutor.AddCommand(Constants.PlayerStop, container.GetInstance<RequestStop>());
            commandExecutor.AddCommand(Constants.PlayerVolume, container.GetInstance<RequestVolume>());
            commandExecutor.AddCommand(Constants.PlayerStatus, container.GetInstance<RequestPlayerStatus>());
            commandExecutor.AddCommand(Constants.PlayerAutoDj, container.GetInstance<RequestAutoDj>());
            commandExecutor.AddCommand(Constants.PlayerShuffle, container.GetInstance<RequestShuffle>());
            commandExecutor.AddCommand(Constants.PlayerScrobble, container.GetInstance<RequestScrobble>());
            commandExecutor.AddCommand(Constants.PlayerRepeat, container.GetInstance<RequestRepeat>());
            commandExecutor.AddCommand(Constants.PlayerMute, container.GetInstance<RequestMute>());
            commandExecutor.AddCommand(Constants.NowPlayingPosition, container.GetInstance<RequestPlaybackPosition>());
            commandExecutor.AddCommand(Constants.NowPlayingListRemove, container.GetInstance<RequestNowPlayingTrackRemoval>());
            commandExecutor.AddCommand(Constants.NowPlayingListPlay, container.GetInstance<RequestNowPlayingPlay>());
            commandExecutor.AddCommand(Constants.NowPlayingList, container.GetInstance<RequestNowPlayingList>());
            commandExecutor.AddCommand(Constants.NowPlayingLfmRating, container.GetInstance<RequestLfmLoveRating>());
            commandExecutor.AddCommand(Constants.NowPlayingTrack, container.GetInstance<RequestSongInfo>());
            commandExecutor.AddCommand(Constants.NowPlayingCover, container.GetInstance<RequestCover>());
            commandExecutor.AddCommand(Constants.NowPlayingLyrics, container.GetInstance<RequestLyrics>());
            commandExecutor.AddCommand(Constants.NowPlayingRating, container.GetInstance<RequestRating>());
            commandExecutor.AddCommand(Constants.NowPlayingListSearch, container.GetInstance<RequestNowPlayingSearch>());
            commandExecutor.AddCommand(Constants.NowPlayingListMove, container.GetInstance<RequestNowPlayingMoveTrack>());
            commandExecutor.AddCommand(Constants.LibrarySearchArtist, container.GetInstance<RequestLibSearchArtist>());
            commandExecutor.AddCommand(Constants.LibrarySearchAlbum, container.GetInstance<RequestLibSearchAlbum>());
            commandExecutor.AddCommand(Constants.LibrarySearchGenre, container.GetInstance<RequestLibSearchGenre>());
            commandExecutor.AddCommand(Constants.LibrarySearchTitle, container.GetInstance<RequestLibSearchTitle>());
            commandExecutor.AddCommand(Constants.LibraryQueueAlbum, container.GetInstance<RequestLibQueueAlbum>());
            commandExecutor.AddCommand(Constants.LibraryQueueArtist, container.GetInstance<RequestLibQueueArtist>());
            commandExecutor.AddCommand(Constants.LibraryQueueGenre, container.GetInstance<RequestLibQueueGenre>());
            commandExecutor.AddCommand(Constants.LibraryQueueTrack, container.GetInstance<RequestLibQueueTrack>());
            commandExecutor.AddCommand(Constants.LibraryArtistAlbums, container.GetInstance<RequestLibArtistAlbums>());
            commandExecutor.AddCommand(Constants.LibraryAlbumTracks, container.GetInstance<RequestLibAlbumTracks>());
            commandExecutor.AddCommand(Constants.LibraryGenreArtists, container.GetInstance<RequestLibGenreArtists>());

            #region Protocol 2.1
            commandExecutor.AddCommand(Constants.Pong, container.GetInstance<HandlePong>());
            commandExecutor.AddCommand(Constants.Ping, container.GetInstance<PingReply>());
            commandExecutor.AddCommand(Constants.Init, container.GetInstance<ProcessInitRequest>());
            commandExecutor.AddCommand(Constants.PlayerPlay, container.GetInstance<RequestPlay>());
            commandExecutor.AddCommand(Constants.PlayerPause, container.GetInstance<RequestPause>());

            #endregion

            #region Protocol 3
            commandExecutor.AddCommand(Constants.PlaylistPlay, container.GetInstance<RequestPlaylistPlay>());
            commandExecutor.AddCommand(Constants.LibraryBrowseGenres, container.GetInstance<RequestBrowseGenres>());
            commandExecutor.AddCommand(Constants.LibraryBrowseArtists, container.GetInstance<RequestBrowseArtists>());
            commandExecutor.AddCommand(Constants.LibraryBrowseAlbums, container.GetInstance<RequestBrowseAlbums>());
            commandExecutor.AddCommand(Constants.LibraryBrowseTracks, container.GetInstance<RequestBrowseTracks>());
            commandExecutor.AddCommand(Constants.NowPlayingQueue, container.GetInstance<RequestNowplayingQueue>());
            #endregion

            #region Protocol 4
            commandExecutor.AddCommand(Constants.PlayerOutput, container.GetInstance<RequestOutputDeviceList>());
            commandExecutor.AddCommand(Constants.PlayerOutputSwitch, container.GetInstance<RequestPlayerOutputSwitch>());
            commandExecutor.AddCommand(Constants.RadioStations, container.GetInstance<RequestRadioStations>());
            #endregion

        }
    }
}