using System.Diagnostics;
using MusicBeePlugin.AndroidRemote.Events;
using MusicBeePlugin.PartyMode.Core;
using TinyMessenger;

namespace MusicBeePlugin.AndroidRemote.Controller
{
    using Interfaces;
    using System;
    using System.Collections.Generic;

    internal class Controller
    {
        private readonly Dictionary<string, ICommand> _commandMap;
        private readonly ITinyMessengerHub _hub;
        private readonly PartyModeCommandHandler _handler;

        public Controller(ITinyMessengerHub hub, PartyModeCommandHandler handler)
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
                }
                else
                {
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