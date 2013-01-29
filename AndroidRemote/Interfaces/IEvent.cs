namespace MusicBeePlugin.AndroidRemote.Interfaces
{
    public interface IEvent
    {
        string Data { get; }
        string Type { get; }
        string ClientId { get; }
        string ExtraData { get; }
    }
}
