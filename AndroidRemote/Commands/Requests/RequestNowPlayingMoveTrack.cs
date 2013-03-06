using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    class RequestNowPlayingMoveTrack : ICommand
    {
        public void Dispose()
        {  
        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.RequestNowPlayingMove(eEvent.ClientId,eEvent.DataToString());
        }
    }
}
