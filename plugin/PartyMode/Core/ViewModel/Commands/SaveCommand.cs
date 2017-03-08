using System;
using System.Windows.Input;
using MusicBeePlugin.PartyMode.Core.Model;
using TinyIoC;

namespace MusicBeePlugin.PartyMode.Core.ViewModel.Commands
{
    public class SaveCommand : ICommand
    {
        private readonly PartyModeModel _model;
        public SaveCommand()
        {
            _model = TinyIoCContainer.Current.Resolve<PartyModeModel>();
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