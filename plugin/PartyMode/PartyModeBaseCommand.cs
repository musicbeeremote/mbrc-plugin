using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.PartyMode
{
    public abstract class PartyModeBaseCommand : ICommand
    {
        public abstract void Execute(IEvent eEvent);
    }
}