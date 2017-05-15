using System;
using MusicBeeRemote.PartyMode.Core.Model;

namespace MusicBeeRemote.PartyMode.Core.ViewModel.Commands
{
    public class SaveCommand
    {
        private readonly PartyModeModel _model;
        public SaveCommand(PartyModeModel model)
        {
            _model = model;
        }

        public void Execute(object parameter)
        {
            _model.SaveSettings();
        }
    }
}