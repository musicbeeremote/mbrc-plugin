using System;
using MusicBeePlugin.Adapters.Contracts;

namespace MusicBeePlugin.Adapters.Implementations
{
    /// <summary>
    ///     Composite adapter that provides access to MusicBee system operations.
    ///     Data access is now provided through DataProviders.
    /// </summary>
    public class MusicBeeApiAdapter : IMusicBeeApiAdapter
    {
        public MusicBeeApiAdapter(ISystemOperations system)
        {
            System = system ?? throw new ArgumentNullException(nameof(system));
        }

        public ISystemOperations System { get; }
    }
}
