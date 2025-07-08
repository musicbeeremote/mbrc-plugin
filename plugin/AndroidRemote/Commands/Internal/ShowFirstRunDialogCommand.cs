using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Settings;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    internal class ShowFirstRunDialogCommand : ICommand
    {
        private readonly ISettingsService _settingsService;

        public ShowFirstRunDialogCommand(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public void Execute(IEvent eEvent)
        {
            if (UserSettings.Instance.IsFirstRun()) _settingsService.OpenInfoWindow();
        }
    }
}