namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    using Interfaces;
    using Utilities;

    internal class RequestScrobble:ICommand
    {
        public void Dispose()
        {
        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestScrobblerState(eEvent.Data.Equals("toggle")?StateAction.Toggle : StateAction.State);
        }
    }
}
