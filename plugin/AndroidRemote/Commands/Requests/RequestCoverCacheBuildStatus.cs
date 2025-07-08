using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestCoverCacheBuildStatus : ICommand
    {
        private readonly ISettingsService _settingsService;

        public RequestCoverCacheBuildStatus(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public void Execute(IEvent eEvent)
        {
            _settingsService.BroadcastCoverCacheBuildStatus(eEvent.ClientId);
        }
    }
}