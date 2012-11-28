using MusicBeePlugin.AndroidRemote.Utilities;
namespace MusicBeePlugin.AndroidRemote.Commands.Replies
{
    using Interfaces;
    using Networking;

    class PShuffleChanged : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(XmlCreator.Create(Constants.Shuffle, eEvent.Data, true, true));
        }
    }
}
