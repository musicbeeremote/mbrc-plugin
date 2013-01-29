namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    using MusicBeePlugin.AndroidRemote.Interfaces;

    class RequestLfmLoveRating : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestLoveStatusChange(eEvent.Data);
        }
    }
}