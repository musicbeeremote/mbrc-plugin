using System;
using System.Collections.Generic;
using MusicBeePlugin.AndroidRemote.Interfaces;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.AndroidRemote.Commands
{
    /// <summary>
    /// Factory for creating command instances with dependency injection
    /// </summary>
    internal class CommandFactory
    {
        private readonly IServiceLocator _serviceLocator;
        private readonly Dictionary<Type, Func<ICommand>> _commandFactories;

        public CommandFactory(IServiceLocator serviceLocator)
        {
            _serviceLocator = serviceLocator;
            _commandFactories = new Dictionary<Type, Func<ICommand>>();
        }

        /// <summary>
        /// Registers a command type with its factory function
        /// </summary>
        /// <typeparam name="T">Command type</typeparam>
        /// <param name="factory">Factory function to create the command</param>
        public void RegisterCommand<T>(Func<ICommand> factory) where T : ICommand
        {
            _commandFactories[typeof(T)] = factory;
        }

        /// <summary>
        /// Registers a command type that has no dependencies (uses default constructor)
        /// </summary>
        /// <typeparam name="T">Command type</typeparam>
        public void RegisterCommand<T>() where T : ICommand, new()
        {
            _commandFactories[typeof(T)] = () => new T();
        }

        /// <summary>
        /// Creates a command instance of the specified type
        /// </summary>
        /// <param name="commandType">Type of command to create</param>
        /// <returns>Command instance</returns>
        public ICommand CreateCommand(Type commandType)
        {
            if (_commandFactories.TryGetValue(commandType, out var factory))
            {
                return factory();
            }

            // Fallback to Activator.CreateInstance for backward compatibility
            return (ICommand)Activator.CreateInstance(commandType);
        }

        /// <summary>
        /// Creates a command instance of the specified type
        /// </summary>
        /// <typeparam name="T">Type of command to create</typeparam>
        /// <returns>Command instance</returns>
        public T CreateCommand<T>() where T : ICommand
        {
            return (T)CreateCommand(typeof(T));
        }

        /// <summary>
        /// Gets a service from the service locator
        /// </summary>
        /// <typeparam name="T">Service type</typeparam>
        /// <returns>Service instance</returns>
        public T GetService<T>() where T : class
        {
            return _serviceLocator.GetService<T>();
        }
    }
}