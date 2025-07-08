using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    internal class SocketStatusChanged : ICommand
    {
        private readonly ISettingsService _settingsService;

        public SocketStatusChanged(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public void Execute(IEvent eEvent)
        {
            _settingsService.UpdateWindowStatus((bool)eEvent.Data);
        }
    }
}