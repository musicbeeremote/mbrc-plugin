namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    using Interfaces;
    class RequestLfmLoveRating : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestLoveStatus(eEvent.DataToString());
        }
    }
}