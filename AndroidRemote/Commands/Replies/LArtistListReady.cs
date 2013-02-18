using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Networking;
using MusicBeePlugin.AndroidRemote.Utilities;

namespace MusicBeePlugin.AndroidRemote.Commands.Replies
{
    class LArtistListReady : ICommand
    {
        public void Dispose()
        {
            
        }

        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(XmlCreator.Create(Constants.LibrarySearch, eEvent.Data, true, true), eEvent.ClientId);
        }
    }
}
