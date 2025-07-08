using System;
using System.Collections.Generic;
using MusicBeePlugin.Services.Interfaces;

namespace MusicBeePlugin.Services
{
    /// <summary>
    /// Simple dependency injection container for managing service instances
    /// </summary>
    public class ServiceContainer : IServiceLocator
    {
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        private readonly Dictionary<Type, Func<object>> _serviceFactories = new Dictionary<Type, Func<object>>();

        /// <summary>
        /// Registers a service instance
        /// </summary>
        /// <typeparam name="T">Service interface type</typeparam>
        /// <param name="implementation">Service implementation instance</param>
        public void RegisterInstance<T>(T implementation) where T : class
        {
            _services[typeof(T)] = implementation;
        }

        /// <summary>
        /// Registers a service factory function
        /// </summary>
        /// <typeparam name="T">Service interface type</typeparam>
        /// <param name="factory">Factory function to create the service</param>
        public void RegisterFactory<T>(Func<T> factory) where T : class
        {
            _serviceFactories[typeof(T)] = () => factory();
        }

        /// <summary>
        /// Registers a service type to be instantiated when requested
        /// </summary>
        /// <typeparam name="TInterface">Service interface type</typeparam>
        /// <typeparam name="TImplementation">Service implementation type</typeparam>
        public void RegisterType<TInterface, TImplementation>() 
            where TInterface : class 
            where TImplementation : class, TInterface, new()
        {
            _serviceFactories[typeof(TInterface)] = () => new TImplementation();
        }

        /// <summary>
        /// Gets a service instance by type
        /// </summary>
        /// <typeparam name="T">Service type to retrieve</typeparam>
        /// <returns>Service instance</returns>
        public T GetService<T>() where T : class
        {
            var serviceType = typeof(T);
            
            // Check if we have a cached instance
            if (_services.TryGetValue(serviceType, out var service))
            {
                return (T)service;
            }
            
            // Check if we have a factory
            if (_serviceFactories.TryGetValue(serviceType, out var factory))
            {
                var instance = (T)factory();
                _services[serviceType] = instance; // Cache the instance
                return instance;
            }
            
            throw new InvalidOperationException($"Service of type {serviceType.Name} is not registered");
        }

        /// <summary>
        /// Checks if a service is registered
        /// </summary>
        /// <typeparam name="T">Service type to check</typeparam>
        /// <returns>True if service is registered</returns>
        public bool IsRegistered<T>() where T : class
        {
            var serviceType = typeof(T);
            return _services.ContainsKey(serviceType) || _serviceFactories.ContainsKey(serviceType);
        }

        /// <summary>
        /// Clears all registered services
        /// </summary>
        public void Clear()
        {
            _services.Clear();
            _serviceFactories.Clear();
        }
    }
}