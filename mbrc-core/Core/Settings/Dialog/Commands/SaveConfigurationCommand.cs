using System;
using System.Windows.Input;

namespace MusicBeeRemote.Core.Settings.Dialog.Commands
{
    public class SaveConfigurationCommand : ICommand
    {
        private readonly PersistanceManager _manager;

        public SaveConfigurationCommand(PersistanceManager manager)
        {
            _manager = manager;
        }

        public void Execute(object parameter)
        {
            _manager.SaveSettings();
            if (_manager.UserSettingsModel.UpdateFirewall)
            {
                _manager.UpdateFirewallRules();
            }
        }

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public event EventHandler CanExecuteChanged;
    }
}