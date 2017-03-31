using NLog;

namespace MusicBeeRemote.Core.Logging
{
    public interface IPluginLogManager
    {
        /// <summary>
        /// Initializes the plugin logging functionality for the supplied log level.
        /// The default development log level is Debug.
        /// </summary>
        /// <param name="logLevel">The current log level</param>
        void Initialize(LogLevel logLevel);
    }
}