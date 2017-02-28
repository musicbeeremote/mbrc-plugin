namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    using Interfaces;

    public class RequestPlaybackPosition : ICommand
    {
        public void Dispose()
        {
        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestPlayPosition(eEvent.DataToString());
        }
    }
}