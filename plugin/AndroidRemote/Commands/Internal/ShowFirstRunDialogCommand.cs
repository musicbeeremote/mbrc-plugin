using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.AndroidRemote.Settings;

namespace MusicBeePlugin.AndroidRemote.Commands.Internal
{
    internal class ShowFirstRunDialogCommand : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            if (UserSettings.Instance.IsFirstRun()) Plugin.Instance.OpenInfoWindow();
        }
    }
}