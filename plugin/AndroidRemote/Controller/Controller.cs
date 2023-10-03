using System;
using System.Collections.Generic;
using System.Diagnostics;
using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Controller
{
    internal class Controller
    {
        private readonly Dictionary<string, Type> _commandMap;


        private Controller()
        {
            _commandMap = new Dictionary<string, Type>();
        }

        public static Controller Instance { get; } = new Controller();

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
            var command = (ICommand)Activator.CreateInstance(commandType);
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