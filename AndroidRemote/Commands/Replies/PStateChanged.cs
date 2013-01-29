

using MusicBeePlugin.AndroidRemote.Utilities;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote.Commands.Replies
{
    class PStateChanged : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(XmlCreator.Create(Constants.PlayState, eEvent.Data, true, true));
        }
    }
}
