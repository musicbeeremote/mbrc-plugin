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
            if(!commandMap.ContainsKey(e.Type)) return;
            Type commandType = commandMap[e.Type];
            using(ICommand command = (ICommand)Activator.CreateInstance(commandType))
            {
                command.Execute(e);
            }
        }

        private Controller()
        {
            commandMap = new Dictionary<string, Type>();
        }
    }
}