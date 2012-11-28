using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands.Requests
{
    internal class RequestVolume : ICommand
    {
        public void Dispose()
        {
        }

        public void Execute(IEvent eEvent)
        {
            int iVolume;
            if (!int.TryParse(eEvent.Data, out iVolume)) return;

            Plugin.Instance.RequestVolumeChange(iVolume);
        }
    }
}