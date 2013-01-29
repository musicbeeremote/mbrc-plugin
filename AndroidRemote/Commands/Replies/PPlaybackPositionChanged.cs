using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.Replies
{
    using Interfaces;
    using Networking;

    class PPlaybackPositionChanged : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(XmlCreator.Create(Constants.PlaybackPosition,eEvent.Data,true,true));
        }
    }
}
