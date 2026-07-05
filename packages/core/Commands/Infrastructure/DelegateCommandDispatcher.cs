using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Infrastructure.Logging.Contracts;

namespace MusicBeePlugin.Commands.Infrastructure
{
    /// <summary>
    ///     Simple delegate-based command dispatcher - replacement for Controller.cs
    /// </summary>
    public sealed class DelegateCommandDispatcher : ICommandDispatcher, ICommandRegistrar
    {
        public delegate Task<bool> AsyncCommandHandler(ICommandContext context);

        // Delegates for command execution
        public delegate bool SyncCommandHandler(ICommandContext context);

        private readonly Dictionary<string, AsyncCommandHandler> _asyncCommands;
        private readonly IPluginLogger _logger;

        private readonly Dictionary<string, SyncCommandHandler> _syncCommands;

        public DelegateCommandDispatcher(IPluginLogger logger)
        {
            _logger = logger;
            _syncCommands = new Dictionary<string, SyncCommandHandler>(StringComparer.Ordinal);
            _asyncCommands = new Dictionary<string, AsyncCommandHandler>(StringComparer.Ordinal);
        }

        /// <summary>
        ///     Get command count for diagnostics
        /// </summary>
        public int CommandCount => _syncCommands.Count + _asyncCommands.Count;


        public bool Execute(ICommandContext context)
        {
            if (context == null || string.IsNullOrEmpty(context.CommandType))
                return false;

            var commandId = context.CommandType;
            try
            {
                if (_syncCommands.TryGetValue(commandId, out var syncHandler))
                    return syncHandler(context);

                if (_asyncCommands.TryGetValue(commandId, out var asyncHandler))
                    // Execute async command on thread pool to avoid sync context deadlocks
                    return Task.Run(async () => await asyncHandler(context).ConfigureAwait(false))
                        .GetAwaiter().GetResult();

                _logger.Debug($"Command not found: {commandId}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error executing command: {commandId}");
                return false;
            }
        }

        public async Task<bool> ExecuteAsync(ICommandContext context)
        {
            if (context == null || string.IsNullOrEmpty(context.CommandType))
                return false;

            var commandId = context.CommandType;
            try
            {
                if (_asyncCommands.TryGetValue(commandId, out var asyncHandler))
                    return await asyncHandler(context);

                if (_syncCommands.TryGetValue(commandId, out var syncHandler))
                    // Execute sync command in task
                    return await Task.Run(() => syncHandler(context));

                _logger.Debug($"Command not found: {commandId}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error executing async command: {commandId}");
                return false;
            }
        }

        public bool HasCommand(string commandId)
        {
            return !string.IsNullOrEmpty(commandId) &&
                   (_syncCommands.ContainsKey(commandId) || _asyncCommands.ContainsKey(commandId));
        }

        /// <summary>
        ///     Explicit implementation of ICommandRegistrar.RegisterCommand
        /// </summary>
        void ICommandRegistrar.RegisterCommand(string command, Func<ICommandContext, bool> handler)
        {
            RegisterCommand(command, new SyncCommandHandler(handler));
        }

        /// <summary>
        ///     Register a typed command handler that automatically deserializes request data.
        ///     The handler receives a typed context with the request already deserialized.
        /// </summary>
        void ICommandRegistrar.RegisterCommand<TRequest>(string command, Func<ITypedCommandContext<TRequest>, bool> handler)
        {
            // Wrap the typed handler in a regular handler that creates the typed context
            RegisterCommand(command, new SyncCommandHandler(context =>
            {
                var typedContext = new TypedCommandContext<TRequest>(context);
                return handler(typedContext);
            }));
        }

        /// <summary>
        ///     Register a synchronous command
        /// </summary>
        public void RegisterCommand(string commandId, SyncCommandHandler handler)
        {
            if (string.IsNullOrEmpty(commandId))
                throw new ArgumentException("Command ID cannot be null or empty", nameof(commandId));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _syncCommands[commandId] = handler;
        }

        /// <summary>
        ///     Register an asynchronous command
        /// </summary>
        public void RegisterCommand(string commandId, AsyncCommandHandler handler)
        {
            if (string.IsNullOrEmpty(commandId))
                throw new ArgumentException("Command ID cannot be null or empty", nameof(commandId));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            _asyncCommands[commandId] = handler;
        }
    }
}
