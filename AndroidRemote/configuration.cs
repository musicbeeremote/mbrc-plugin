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
            /** Protocol Related commands **/
            controller.AddCommand(Constants.Player, typeof (RequestPlayer));
            controller.AddCommand(Constants.Protocol, typeof (RequestProtocol));
            controller.AddCommand(Constants.PlayerNext, typeof (RequestNextTrack));
            controller.AddCommand(Constants.PlayerPlayPause, typeof (RequestPlayPause));
            controller.AddCommand(Constants.PlayerPrevious, typeof (RequestPreviousTrack));
            controller.AddCommand(Constants.PlayerStop, typeof (RequestStop));
            controller.AddCommand(Constants.PlayerVolume, typeof (RequestVolume));
            controller.AddCommand(Constants.NowPlayingTrack, typeof (RequestSongInfo));
            controller.AddCommand(Constants.NowPlayingCover, typeof (RequestCover));
            controller.AddCommand(Constants.NowPlayingLyrics, typeof (RequestLyrics));
            controller.AddCommand(Constants.NowPlayingRating, typeof (RequestRating));
            controller.AddCommand(Constants.PlayerStatus, typeof (RequestPlayerStatus));
            controller.AddCommand(Constants.NowPlayingPosition, typeof (RequestPlaybackPosition));
            controller.AddCommand(Constants.NowPlayingListRemove, typeof (RequestNowPlayingTrackRemoval));
            controller.AddCommand(Constants.PlayerAutoDj, typeof (RequestAutoDj));
            controller.AddCommand(Constants.PluginVersion, typeof (RequestPluginVersion));
            controller.AddCommand(Constants.NowPlayingLfmRating, typeof (RequestLfmLoveRating));
            controller.AddCommand(Constants.PlaylistList, typeof (RequestPlaylistList));
            controller.AddCommand(Constants.PlayerShuffle, typeof(RequestShuffle));
            controller.AddCommand(Constants.PlayerScrobble, typeof(RequestScrobble));
            controller.AddCommand(Constants.PlayerRepeat, typeof(RequestRepeat));
            controller.AddCommand(Constants.PlayerMute, typeof(RequestMute));
            controller.AddCommand(Constants.NowPlayingListSearch, typeof(RequestNowPlayingSearch));
            controller.AddCommand(Constants.NowPlayingListMove, typeof(RequestNowPlayingMoveTrack));
            controller.AddCommand(Constants.NowPlayingListQueue, typeof(RequestNowPlayingQueue));
            controller.AddCommand(EventType.ReplyAvailable, typeof(ReplayAvailable));
        }
    }
}