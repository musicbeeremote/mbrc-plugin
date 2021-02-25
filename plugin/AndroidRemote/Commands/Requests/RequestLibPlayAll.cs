using MusicBeePlugin.AndroidRemote.Interfaces;
using ServiceStack;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    class RequestLibPlayAll : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.LibraryPlayAll(eEvent.ClientId, eEvent.Data.ConvertTo<bool>());
        }
    }
}
