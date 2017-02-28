using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model;

namespace MusicBeePlugin.AndroidRemote.Commands.State
{
    internal class PCoverChanged:ICommand
    {
        public void Dispose()
        {
            
        }

        public void Execute(IEvent eEvent)
        {
            LyricCoverModel.Instance.SetCover(eEvent.DataToString());
        }
    }
}
