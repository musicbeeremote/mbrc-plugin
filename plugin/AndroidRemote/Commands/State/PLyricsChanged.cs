using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Model;

namespace MusicBeePlugin.AndroidRemote.Commands.State
{
    /// <summary>
    /// </summary>
    public class PLyricsChanged : ICommand
    {
        /// <summary>
        /// </summary>
        /// <param name="eEvent"></param>
        public void Execute(IEvent eEvent)
        {
            LyricCoverModel.Instance.Lyrics = eEvent.DataToString();
        }
    }
}