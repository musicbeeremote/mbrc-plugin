using System;
using System.Collections.Generic;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Events.Status.Internal;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using NLog;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands
{
    public class CommandExecutor
    {
        private readonly Dictionary<string, ICommand> _commandMap = new Dictionary<string, ICommand>();
        private readonly ITinyMessengerHub _hub;
        private readonly ClientManager _clientManager;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        public CommandExecutor(ITinyMessengerHub hub, ClientManager clientManager)
        {
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            _clientManager = clientManager;
            _hub.Subscribe<MessageEvent>(CommandExecute);
        }

        public void AddCommand(string eventType, ICommand command)
        {
            if (_commandMap.ContainsKey(eventType))
            {
                return;
            }

            _commandMap.Add(eventType, command);
        }

        public void RemoveCommand(string eventType)
        {
            if (_commandMap.ContainsKey(eventType))
            {
                _commandMap.Remove(eventType);
            }
        }

        private void CommandExecute(IEvent e)
        {
            if (e?.Type == null)
            {
                _logger.Debug($"failed to execute command due to missing type/or event: {e}");
                return;
            }

            if (!_commandMap.ContainsKey(e.Type))
            {
                return;
            }

            var command = _commandMap[e.Type];
            try
            {
                Execute(e, command);
            }
            catch (Exception ex)
            {
                _logger.Debug($"{ex.Message}\n{ex.StackTrace}");
            }
        }

        private void Execute(IEvent e, ICommand command)
        {
            if (_clientManager.PermissionMode)
            {
                PermissionBasedExecution(e, command);
            }
            else
            {
                _logger.Debug($"Running {command.GetType()}");
                command.Execute(e);
            }
        }

        private void PermissionBasedExecution(IEvent e, ICommand command)
        {
            if (command.GetType().IsSubclassOf(typeof(LimitedCommand)))
            {
                var limited = (LimitedCommand)command;
                var status = limited.Execute(e, _clientManager.ClientPermissions(e.ClientId));
                _clientManager.Log(limited.Name(), status, e.ClientId);
                if (status != ExecutionStatus.Denied)
                {
                    return;
                }

                var message = new SocketMessage(Constants.CommandUnavailable);
                _hub.Publish(new PluginResponseAvailableEvent(message));
            }
            else
            {
                command.Execute(e);
            }
        }
    }
}
