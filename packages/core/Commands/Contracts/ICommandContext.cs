namespace MusicBeePlugin.Commands.Contracts
{
    /// <summary>
    ///     Represents the context for command execution
    /// </summary>
    public interface ICommandContext
    {
        /// <summary>
        ///     The type of command being executed
        /// </summary>
        string CommandType { get; }

        /// <summary>
        ///     The command data payload
        /// </summary>
        object Data { get; }

        /// <summary>
        ///     The connection identifier that sent the command
        /// </summary>
        string ConnectionId { get; }

        /// <summary>
        ///     A short identifier for logging purposes (first 6 chars of connectionId:clientId)
        /// </summary>
        string ShortId { get; }

        /// <summary>
        ///     Attempts to get the data as the specified type, handling JSON conversions
        /// </summary>
        /// <typeparam name="T">The type to convert to</typeparam>
        /// <param name="value">The converted value</param>
        /// <returns>True if conversion succeeded, false otherwise</returns>
        bool TryGetData<T>(out T value);

        /// <summary>
        ///     Gets the data as the specified type, handling JSON conversions
        /// </summary>
        /// <typeparam name="T">The type to convert to</typeparam>
        /// <param name="defaultValue">Default value if conversion fails</param>
        /// <returns>The converted value or default</returns>
        T GetDataOrDefault<T>(T defaultValue = default);
    }
}
