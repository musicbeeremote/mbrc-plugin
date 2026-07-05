namespace MusicBeePlugin.Adapters.Contracts
{
    /// <summary>
    ///     Composite adapter that provides access to MusicBee system operations.
    ///     Data access is now provided through IDataProviders.
    /// </summary>
    public interface IMusicBeeApiAdapter
    {
        ISystemOperations System { get; }
    }
}
