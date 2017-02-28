namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    using Interfaces;
    using Utilities;

    internal class RequestRepeat:ICommand
    {
        public void Dispose()
        {
        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestRepeatState(eEvent.Data.Equals("toggle")?StateAction.Toggle : StateAction.State);
        }
    }
}
