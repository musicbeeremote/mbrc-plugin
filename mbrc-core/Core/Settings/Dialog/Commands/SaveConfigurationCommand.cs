namespace MusicBeeRemote.Core.Settings.Dialog.Commands
{
    public class SaveConfigurationCommand
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
    }
}