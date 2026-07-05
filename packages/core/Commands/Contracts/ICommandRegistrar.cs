using System;

namespace MusicBeePlugin.Commands.Contracts
{
    /// <summary>
    ///     Interface for registering commands with the command dispatcher
    /// </summary>
    public interface ICommandRegistrar
    {
        /// <summary>
        ///     Register a command handler for a specific command string
        /// </summary>
        /// <param name="command">The command string to register</param>
        /// <param name="handler">The handler function to execute for this command</param>
        void RegisterCommand(string command, Func<ICommandContext, bool> handler);

        /// <summary>
        ///     Register a typed command handler that automatically deserializes request data.
        ///     The dispatcher will automatically wrap the context in a typed context and
        ///     handle deserialization of the request payload.
        /// </summary>
        /// <typeparam name="TRequest">The expected request type for this command</typeparam>
        /// <param name="command">The command string to register</param>
        /// <param name="handler">The handler function receiving a typed context</param>
        void RegisterCommand<TRequest>(string command, Func<ITypedCommandContext<TRequest>, bool> handler);
    }
}
