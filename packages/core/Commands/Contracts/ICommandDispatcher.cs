using System.Threading.Tasks;

namespace MusicBeePlugin.Commands.Contracts
{
    /// <summary>
    ///     Simple command dispatcher interface for delegate-based command execution
    /// </summary>
    public interface ICommandDispatcher
    {
        /// <summary>
        ///     Execute a command synchronously using the command type from context
        /// </summary>
        /// <param name="context">The command context containing command type, data, and client info</param>
        /// <returns>True if command was found and executed successfully</returns>
        bool Execute(ICommandContext context);

        /// <summary>
        ///     Execute a command asynchronously using the command type from context
        /// </summary>
        /// <param name="context">The command context containing command type, data, and client info</param>
        /// <returns>True if command was found and executed successfully</returns>
        Task<bool> ExecuteAsync(ICommandContext context);

        /// <summary>
        ///     Check if a command is registered
        /// </summary>
        /// <param name="commandId">The command identifier</param>
        /// <returns>True if command exists</returns>
        bool HasCommand(string commandId);
    }
}
