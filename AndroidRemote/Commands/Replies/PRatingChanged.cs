namespace MusicBeePlugin.AndroidRemote.Commands.Replies
{
    using MusicBeePlugin.AndroidRemote.Interfaces;
    using MusicBeePlugin.AndroidRemote.Networking;
    using MusicBeePlugin.AndroidRemote.Utilities;

    class PRatingChanged : ICommand
    {
        public void Dispose()
        {
        }

        public void Execute(IEvent eEvent)
        {
            SocketServer.Instance.Send(XmlCreator.Create(Constants.Rating, eEvent.Data, true, true), eEvent.ClientId);
        }
    }
}
