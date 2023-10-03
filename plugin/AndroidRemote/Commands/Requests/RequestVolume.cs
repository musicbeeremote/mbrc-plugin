using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestVolume : ICommand
    {
        public void Execute(IEvent eEvent)
        {
            if (!int.TryParse(eEvent.DataToString(), out var iVolume)) return;

            Plugin.Instance.RequestVolumeChange(iVolume);
        }
    }
}