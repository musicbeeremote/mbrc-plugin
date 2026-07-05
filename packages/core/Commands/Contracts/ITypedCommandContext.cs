namespace MusicBeePlugin.Commands.Contracts
{
    /// <summary>
    ///     Represents a command context with strongly-typed request data.
    ///     Provides compile-time type safety for command handlers.
    /// </summary>
    /// <typeparam name="TRequest">The expected request type for this command</typeparam>
    public interface ITypedCommandContext<out TRequest> : ICommandContext
    {
        /// <summary>
        ///     Gets the strongly-typed request data.
        ///     Returns the deserialized request object.
        /// </summary>
        TRequest TypedData { get; }

        /// <summary>
        ///     Indicates whether the typed data was successfully deserialized
        ///     and passes any validation defined on the request type.
        /// </summary>
        bool IsValid { get; }
    }
}
