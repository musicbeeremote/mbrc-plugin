using MusicBeePlugin.AndroidRemote.Model;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.InstaReplies
{
    using Interfaces;
    using Networking;

    class RequestLyrics : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(XmlCreator.Create(Constants.Lyrics, LyricCoverModel.Instance.Lyrics, true, true), eEvent.ClientId);
            
        }
    }
}