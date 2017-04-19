using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;

namespace MusicBeeRemote.Core.Settings.Dialog.Commands
{
    public class RemoveAddressFromWhiteListCommand : ICommand
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
