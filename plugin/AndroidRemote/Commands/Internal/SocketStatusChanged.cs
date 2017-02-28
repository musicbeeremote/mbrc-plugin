using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    internal class SocketStatusChanged:ICommand
    {
        public void Dispose()
        {
        }

        public void Execute(IEvent eEvent)
        {
            Plugin.Instance.UpdateWindowStatus((bool)eEvent.Data);
        }
    }
}
