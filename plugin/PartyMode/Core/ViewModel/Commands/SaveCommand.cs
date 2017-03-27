using System;
using System.Windows.Input;
using MusicBeePlugin.PartyMode.Core.Model;

namespace MusicBeePlugin.PartyMode.Core.ViewModel.Commands
{
    public class SaveCommand : ICommand
    {
        private readonly PartyModeModel _model;
        public SaveCommand(PartyModeModel model)
        {
            _model = model;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public void Execute(object parameter)
        {
            _model.SaveSettings();
        }
    }
}