using System.Diagnostics;

namespace MusicBeeRemote.Core.Settings.Dialog.Commands
{
    public class OpenHelpCommand
    {
        public void Execute()
        {
            Process.Start("http://kelsos.net/musicbeeremote/help/");
        }        
    }
}