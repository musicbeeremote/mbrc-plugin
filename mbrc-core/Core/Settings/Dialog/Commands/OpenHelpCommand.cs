using System.Diagnostics;

namespace MusicBeeRemote.Core.Settings.Dialog.Commands
{
    public class OpenHelpCommand
    {
        public void Execute(object parameter)
        {
            Process.Start("http://kelsos.net/musicbeeremote/help/");
        }        
    }
}