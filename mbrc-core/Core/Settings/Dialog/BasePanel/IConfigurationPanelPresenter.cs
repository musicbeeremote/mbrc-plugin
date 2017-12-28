namespace MusicBeeRemote.Core.Settings.Dialog.BasePanel
{
    public interface IConfigurationPanelPresenter
    {
        void Load();
        void Attach(IConfigurationPanelView view);
        void OpenHelp();
        void SaveSettings();
        void OpenLogDirectory();
        void LoggingStatusChanged(bool @checked);
        void UpdateFirewallSettingsChanged(bool @checked);
        void UpdateListeningPort(uint listeningPort);
        void UpdateFilteringSelection(FilteringSelection selected);

    }
}