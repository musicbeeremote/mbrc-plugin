using System;
using System.Windows.Input;
using MusicBeePlugin.PartyMode.Core.Model;

namespace MusicBeePlugin.PartyMode.Core.ViewModel.Commands
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