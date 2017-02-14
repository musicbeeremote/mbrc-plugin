using System.Diagnostics;
using System.Threading;

namespace MusicBeePlugin.AndroidRemote.Controller
{
    using Commands;
    using Interfaces;
    using System;
    using System.Collections.Generic;

    internal class Controller
    {
        private readonly Dictionary<string, Type> _commandMap;
        public static Controller Instance { get; } = new Controller();

        private Controller()
        {
            _commandMap = new Dictionary<string, Type>();
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
            var command = (ICommand)Activator.CreateInstance(commandType);
            try
            {
                PartyModeCommandDedcorator cmdDecorator = new PartyModeCommandDedcorator(command);
                cmdDecorator.Execute(e);
            }
            catch (Exception ex)
            {
#if DEBUG
                Debug.WriteLine(ex.Message.ToString()+ "\n"+ ex.StackTrace.ToString());
#endif
                // Oh noes something went wrong... let's ignore the exception?
            }
        }

      
    }
}