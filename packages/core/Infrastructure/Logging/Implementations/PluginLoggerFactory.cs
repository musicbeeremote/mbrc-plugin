using System;
using MusicBeePlugin.Infrastructure.Logging.Contracts;

namespace MusicBeePlugin.Infrastructure.Logging.Implementations
{
    /// <summary>
    ///     Factory for creating plugin logger instances.
    /// </summary>
    public static class PluginLoggerFactory
    {
        /// <summary>
        ///     Creates a logger for the specified type.
        /// </summary>
        /// <param name="type">The type to create a logger for.</param>
        /// <returns>A logger instance.</returns>
        public static IPluginLogger CreateLogger(Type type)
        {
            return new PluginLogger(type);
        }

        /// <summary>
        ///     Creates a logger for the specified type.
        /// </summary>
        /// <typeparam name="T">The type to create a logger for.</typeparam>
        /// <returns>A logger instance.</returns>
        public static IPluginLogger CreateLogger<T>()
        {
            return new PluginLogger(typeof(T));
        }

        /// <summary>
        ///     Creates a logger with the specified name.
        /// </summary>
        /// <param name="name">The logger name.</param>
        /// <returns>A logger instance.</returns>
        public static IPluginLogger CreateLogger(string name)
        {
            return new PluginLogger(name);
        }
    }
}
