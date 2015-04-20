namespace MusicBeePlugin.AndroidRemote.Interfaces
{
    interface ICommand
    {
        void Execute(IEvent eEvent);
    }
}
