namespace MusicBeePlugin.AndroidRemote.Commands.InstaReplies
{
    using Entities;
    using Model;
    using Interfaces;
    using Networking;

    class RequestLyrics : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(new SocketMessage(Constants.NowPlayingLyrics,Constants.Reply, LyricCoverModel.Instance.Lyrics).toJsonString(), eEvent.ClientId);
        }
    }
}