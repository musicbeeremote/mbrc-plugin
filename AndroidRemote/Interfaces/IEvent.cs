namespace MusicBeePlugin.AndroidRemote.Interfaces
{
    interface IEvent
    {
        string GetEventData();
        IEventType GetEventType();
    }
}
