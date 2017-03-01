using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model;

namespace MusicBeePlugin.AndroidRemote.Commands.State
{
    internal class UpdateCoverCommand:ICommand
    {
        public void Execute(IEvent eEvent)
        {
            LyricCoverModel.Instance.SetCover(eEvent.DataToString());
        }
    }

    public class UpdateLyricsCommand:ICommand
    {
        public void Execute(IEvent eEvent)
        {
            LyricCoverModel.Instance.Lyrics = eEvent.DataToString();
        }
    }
}