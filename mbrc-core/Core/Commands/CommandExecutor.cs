using System;
using System.Collections.Generic;
using System.Diagnostics;
using MusicBeeRemote.Core.Events;
using MusicBeeRemote.Core.Model.Entities;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.PartyMode.Core;
using TinyMessenger;

namespace MusicBeeRemote.Core.Commands
{
    public class CommandExecutor
    {
        private readonly Dictionary<string, ICommand> _commandMap;
        private readonly ITinyMessengerHub _hub;
        private readonly PartyModeCommandHandler _handler;

        public CommandExecutor(ITinyMessengerHub hub, PartyModeCommandHandler handler)
        {
            _hub = hub;
            _handler = handler;
            _commandMap = new Dictionary<string, ICommand>();
            _hub.Subscribe<MessageEvent>(CommandExecute);
        }

        public void AddCommand(string eventType, ICommand command)
        {
            if (_commandMap.ContainsKey(eventType)) return;
            _commandMap.Add(eventType, command);
        }

        public void RemoveCommand(string eventType)
        {
            if (_commandMap.ContainsKey(eventType))
                _commandMap.Remove(eventType);
        }

        public void CommandExecute(IEvent e)
        {
            if (e?.Type == null)
            {
#if DEBUG
                Debug.WriteLine("failed to execute command due to missing type/or event");
#endif
                return;
            }

            if (!_commandMap.ContainsKey(e.Type)) return;
            var command = _commandMap[e.Type];
            try
            {
                if (_handler.PartyModeActive)
                {
                    if (_handler.HasPermissions(command, e))
                    {
                        command.Execute(e);
                    }
                    else
                    {
                        var message = new SocketMessage(Constants.CommandUnavailable);
                        _hub.Publish(new PluginResponseAvailableEvent(message));
                    }
                }
                else
                {
                    Debug.WriteLine($"Running {command.GetType()}");
                    command.Execute(e);
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine(ex.Message + "\n" + ex.StackTrace);
#endif
                // Oh noes something went wrong... let's ignore the exception?
            }
        }
    }
}