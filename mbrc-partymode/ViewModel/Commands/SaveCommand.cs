using System;
using System.Windows.Input;
using MbrcPartyMode.Model;

namespace MbrcPartyMode.ViewModel.Commands
{
    public class PartyModeCommands
    {
    }

    public class SaveCommand : ICommand
    {
        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public void Execute(object parameter)
        {
            PartyModeModel.Instance.SaveSettings();
        }
    }
}