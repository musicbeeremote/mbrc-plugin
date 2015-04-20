
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
            var message =
                new SocketMessage(Constants.NowPlayingCover, LyricCoverModel.Instance.Cover)
                    .ToJsonString();
            SocketServer.Instance.Send(message, eEvent.ClientId);
        }
    }
}
