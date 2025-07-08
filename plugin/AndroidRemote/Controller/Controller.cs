using System;
using System.Collections.Generic;
using System.Diagnostics;
using MusicBeePlugin.AndroidRemote.Commands;
using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Controller
{
    internal class Controller
    {
        private readonly Dictionary<string, Type> _commandMap;
        private CommandFactory _commandFactory;

        private Controller()
        {
            _commandMap = new Dictionary<string, Type>();
        }

        public static Controller Instance { get; } = new Controller();

        /// <summary>
        /// Sets the command factory for dependency injection
        /// </summary>
        /// <param name="commandFactory">Command factory instance</param>
        public void SetCommandFactory(CommandFactory commandFactory)
        {
            _commandFactory = commandFactory;
        }

        public void AddCommand(string eventType, Type command)
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
            var commandType = _commandMap[e.Type];
            
            // Use command factory if available, otherwise fallback to Activator
            var command = _commandFactory?.CreateCommand(commandType) ?? (ICommand)Activator.CreateInstance(commandType);
            
            try
            {
                command.Execute(e);
            }
            catch (Exception)
            {
                // Oh noes something went wrong... let's ignore the exception?
            }
        }
    }
}