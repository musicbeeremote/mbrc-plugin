using System;
using System.Linq;
using Autofac;
using MusicBeePlugin.Infrastructure.Logging.Implementations;

namespace MusicBeePlugin.Infrastructure.DependencyInjection
{
    /// <summary>
    ///     Autofac module for registering logging components.
    ///     Contains logger factory and logging configuration.
    /// </summary>
    public class LoggingModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            // Register logger factory for dependency injection
            builder.Register((context, parameters) =>
            {
                // Try to get the requesting type from parameters, fallback to generic logger
                var requestingType = parameters.OfType<TypedParameter>()
                    .FirstOrDefault(p => p.Type == typeof(Type))?.Value as Type;

                return requestingType != null
                    ? PluginLoggerFactory.CreateLogger(requestingType)
                    : PluginLoggerFactory.CreateLogger("MusicBeePlugin");
            }).InstancePerDependency();
        }
    }
}
