using System;
using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    class SocketStatusChanged:ICommand
    {
        public void Dispose()
        {
        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.UpdateWindowStatus((Boolean)eEvent.Data);
        }
    }
}
