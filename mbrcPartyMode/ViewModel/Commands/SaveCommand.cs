using System;
using mbrcPartyMode.Model;
using System.Windows.Input;

namespace mbrcPartyMode.ViewModel.Commands
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