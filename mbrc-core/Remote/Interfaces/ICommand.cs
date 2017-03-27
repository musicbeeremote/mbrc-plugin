namespace MusicBeeRemoteCore.Remote.Interfaces
{
    public interface ICommand
    {
        void Execute(IEvent @event);
    }
}