using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestRating : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestTrackRating(eEvent.DataToString(), eEvent.ClientId);
        }
    }
}