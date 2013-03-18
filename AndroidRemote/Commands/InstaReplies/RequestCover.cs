
namespace MusicBeePlugin.AndroidRemote.Commands.InstaReplies
{
    using Entities;
    using Interfaces;
    using Model;
    using Networking;

    class RequestCover : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(new SocketMessage(Constants.NowPlayingCover, Constants.Reply, LyricCoverModel.Instance.Cover).toJsonString());
        }
    }
}
