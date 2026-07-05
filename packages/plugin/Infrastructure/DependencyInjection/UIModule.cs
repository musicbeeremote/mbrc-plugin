using System;
using Autofac;
using MusicBeePlugin.Adapters.Contracts;
using MusicBeePlugin.Services.Core;
using MusicBeePlugin.Services.Media;
using MusicBeePlugin.Services.UI;
using MusicBeePlugin.UI.Windows;

namespace MusicBeePlugin.Infrastructure.DependencyInjection
{
    /// <summary>
    ///     Autofac module for registering UI-related components.
    ///     Contains windows, dialogs, and user interface services.
    /// </summary>
    public class UIModule : Module
    {
        private readonly IPluginCore _pluginCore;

        public UIModule(IPluginCore pluginCore)
        {
            _pluginCore = pluginCore;
        }

        protected override void Load(ContainerBuilder builder)
        {
            // Register InfoWindow (from UI.Windows namespace)
            builder.RegisterType<InfoWindow>()
                .As<IInfoWindow>()
                .InstancePerDependency();

            // Register WindowManager with factory that will be resolved when needed
            builder.Register<IWindowManager>(c =>
                {
                    var systemAdapter = c.Resolve<ISystemOperations>();
                    var coverService = c.Resolve<ICoverService>();
                    var infoWindowFactory = c.Resolve<Func<IInfoWindow>>();
                    return new WindowManager(systemAdapter, coverService, infoWindowFactory, _pluginCore);
                })
                .SingleInstance();
        }
    }
}
