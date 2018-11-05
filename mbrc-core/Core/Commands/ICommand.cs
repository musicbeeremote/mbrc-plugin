using MusicBeeRemote.Core.Events;

namespace MusicBeeRemote.Core.Commands
{
    public interface ICommand
    {
        void Execute(IEvent @event);
    }
}