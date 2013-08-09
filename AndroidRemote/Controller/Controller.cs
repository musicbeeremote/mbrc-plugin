using System.Threading;

namespace MusicBeePlugin.AndroidRemote.Controller
{
    using System;
    using System.Collections.Generic;
    using Interfaces;
 
    internal class Controller
    {
        private readonly Dictionary<string, Type> commandMap; 

        private static readonly Controller ClassInstance = new Controller();

        public static Controller Instance
        {
            get { return ClassInstance; }
        }

        public void AddCommand(string eventType,Type command)
        {
            if (commandMap.ContainsKey(eventType)) return;
            commandMap.Add(eventType,command);
        }

        public void RemoveCommand(string eventType)
        {
            if (commandMap.ContainsKey(eventType))
                commandMap.Remove(eventType);    
        }

        public void CommandExecute(IEvent e)
        {
            ThreadPool.QueueUserWorkItem(BgCommandExecutor, e);
        }

        private void BgCommandExecutor(object e)
        {
            if (!commandMap.ContainsKey(((IEvent)e).Type)) return;
            Type commandType = commandMap[((IEvent)e).Type];
            using (ICommand command = (ICommand)Activator.CreateInstance(commandType))
            {
                command.Execute((IEvent)e);
            }
        }

        private Controller()
        {
            commandMap = new Dictionary<string, Type>();
        }
    }
}