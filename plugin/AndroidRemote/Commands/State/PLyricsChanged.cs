namespace MusicBeePlugin.AndroidRemote.Commands.State
{
    using MusicBeePlugin.AndroidRemote.Interfaces;
    using MusicBeePlugin.AndroidRemote.Model;

    /// <summary>
    /// 
    /// </summary>
    public class PLyricsChanged:ICommand
    {
        public void Dispose()
        {
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="eEvent"></param>
        public void Execute(IEvent eEvent)
        {
            LyricCoverModel.Instance.Lyrics = eEvent.DataToString();
        }
    }
}