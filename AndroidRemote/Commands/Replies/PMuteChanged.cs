using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.Replies
{
    using Interfaces;
    using Networking;

    class PMuteChanged : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(XmlCreator.Create(Constants.Mute, eEvent.Data, true, true));
        }
    }
}
