using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    public class RequestPlaybackPosition : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPlayPosition(eEvent.DataToString());
        }
    }
}