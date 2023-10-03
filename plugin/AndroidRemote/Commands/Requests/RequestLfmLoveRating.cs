using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestLfmLoveRating : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestLoveStatus(eEvent.DataToString(), eEvent.ClientId);
        }
    }
}