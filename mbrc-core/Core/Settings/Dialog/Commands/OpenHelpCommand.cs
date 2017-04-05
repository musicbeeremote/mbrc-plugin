using System;
using System.Diagnostics;
using System.Windows.Input;

namespace MusicBeeRemote.Core.Settings.Dialog.Commands
{
    public class OpenHelpCommand : ICommand
    {
        public void Execute(object parameter)
        {
            Process.Start("http://kelsos.net/musicbeeremote/help/");
        }

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public event EventHandler CanExecuteChanged;
    }
}