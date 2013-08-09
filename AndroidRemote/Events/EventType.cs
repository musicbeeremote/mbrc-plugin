namespace MusicBeePlugin.AndroidRemote.Events
{
    class EventType
    {
        public const string ActionClientConnected = "ActionClientConnected";
        public const string ActionClientDisconnected = "ActionClientDisconnected";
        public const string ActionForceClientDisconnect = "ActionForceClientDisconnect";
        public const string ActionDataAvailable = "ActionDataAvailable";
        public const string ActionSocketStart = "ActionSocketStart";
        public const string ActionSocketStop = "ActionSocketStop";
        public const string InitializeModel = "InitializeModel";
        public const string ReplyAvailable = "ReplyAvailable";
        public const string StartServiceBroadcast = "StartServiceBroadcast";
        public const string RestartSocket = "RestartSocket";

        public const string NowPlayingCoverChange = "NowPlayingCoverChange";
        public const string NowPlayingLyricsChange = "NowPlayingLyricsChange";
        public const string SocketStatusChange = "SocketStatusChange";
        public const string ShowFirstRunDialog = "ShowFirstRunDialog";
    }
}
