

using MusicBeePlugin.AndroidRemote.Utilities;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;

namespace MusicBeePlugin.AndroidRemote.Commands.Replies
{
    class PPlaylistReady : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(XmlCreator.Create(Constants.PlaylistList,eEvent.Data,true,true),eEvent.ClientId);
        }
    }
}
