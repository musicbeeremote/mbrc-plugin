namespace MusicBeePlugin.AndroidRemote.Commands.Replies
{
    using Interfaces;

    using MusicBeePlugin.AndroidRemote.Utilities;

    using Networking;

    class PLfmLoveRating : ICommand
    {
        public void Dispose()
        {

        }

        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(XmlCreator.Create(Constants.LfmLoveRating, eEvent.Data, true, true));
        }
    }
}
