using System;

namespace MusicBeePlugin.AndroidRemote.Interfaces
{
    interface ICommand:IDisposable
    {
        void Execute(IEvent eEvent);
    }
}
