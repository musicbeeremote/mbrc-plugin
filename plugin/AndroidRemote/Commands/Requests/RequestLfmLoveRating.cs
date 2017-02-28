namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    using Interfaces;

    internal class RequestLfmLoveRating : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestLoveStatus(eEvent.DataToString(), eEvent.ClientId);
        }
    }
}