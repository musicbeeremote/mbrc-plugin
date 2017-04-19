using System;
using System.Windows.Input;

namespace MusicBeeRemote.Core.Settings.Dialog.Commands
{
    public class AddAddressToWhiteListCommand : ICommand
    {
        public void Execute(object parameter)
        {
            
        }

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public event EventHandler CanExecuteChanged;
    }
}
