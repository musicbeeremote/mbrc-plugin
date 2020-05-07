namespace MusicBeeRemote.Core.Settings.Dialog.BasePanel
{
    public interface IConfigurationPanelPresenter
    {
        void Load();

        void Attach(IConfigurationPanelView view);

        void OpenHelp();

        void SaveSettings();

        void OpenLogDirectory();

        void LoggingStatusChanged(bool enabled);

        void UpdateFirewallSettingsChanged(bool enabled);

        void UpdateListeningPort(uint listeningPort);

        void UpdateFilteringSelection(FilteringSelection selected);

        void RefreshCache();
    }
}
