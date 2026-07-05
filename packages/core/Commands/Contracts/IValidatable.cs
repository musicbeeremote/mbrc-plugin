namespace MusicBeePlugin.Commands.Contracts
{
    /// <summary>
    ///     Interface for request types that support validation.
    ///     Implement this interface on request DTOs to enable automatic
    ///     validation when used with typed command contexts.
    /// </summary>
    public interface IValidatable
    {
        /// <summary>
        ///     Returns true if the request data passes validation.
        /// </summary>
        bool IsValid { get; }
    }
}
