namespace MusicBeeRemote.Core.Settings.Dialog.Commands
{
    public class SaveConfigurationCommand
    {
        private readonly PersistenceManager _manager;

        public SaveConfigurationCommand(PersistenceManager manager)
        {
            _manager = manager;
        }

        public void Execute()
        {
            _manager.SaveSettings();
            if (_manager.UserSettingsModel.UpdateFirewall)
            {
                _manager.UpdateFirewallRules();
            }
        }
    }
}
