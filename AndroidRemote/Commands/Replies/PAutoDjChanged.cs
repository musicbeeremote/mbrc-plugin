using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.Replies
{
    using Interfaces;
    using Networking;

    class PAutoDjChanged : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(XmlCreator.Create(Constants.AutoDj, eEvent.Data, true, true));
        }
    }
}
