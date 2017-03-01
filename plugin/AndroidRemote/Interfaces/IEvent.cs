namespace MusicBeePlugin.AndroidRemote.Interfaces
{
    public interface IEvent
    {
        object Data { get; }
        string Type { get; }
        string ConnectionId { get; }
        string ExtraData { get; }
        string DataToString();
    }
}
