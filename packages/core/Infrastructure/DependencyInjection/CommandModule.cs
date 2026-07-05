using Autofac;
using MusicBeePlugin.Commands.Contracts;
using MusicBeePlugin.Commands.Handlers;
using MusicBeePlugin.Commands.Infrastructure;

namespace MusicBeePlugin.Infrastructure.DependencyInjection
{
    /// <summary>
    ///     Autofac module for registering command system components.
    ///     Contains command dispatcher, command delegates, and command registration logic.
    /// </summary>
    public class CommandModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            // Register Command Dispatcher
            builder.RegisterType<DelegateCommandDispatcher>()
                .As<ICommandDispatcher>()
                .SingleInstance();

            // Register Command Classes (for DI-enabled commands)
            // Note: IMusicBeeApiAdapter will be registered manually in PluginCore
            builder.RegisterType<PlayerCommands>()
                .AsSelf()
                .SingleInstance();

            builder.RegisterType<NowPlayingCommands>()
                .AsSelf()
                .SingleInstance();

            builder.RegisterType<PlaylistCommands>()
                .AsSelf()
                .SingleInstance();

            builder.RegisterType<SystemCommands>()
                .AsSelf()
                .SingleInstance();

            builder.RegisterType<LibraryCommands>()
                .AsSelf()
                .SingleInstance();

            // Register commands with dispatcher (called after container is built)
            builder.RegisterBuildCallback(container =>
            {
                var dispatcher = container.Resolve<ICommandDispatcher>() as DelegateCommandDispatcher;
                var playerCommands = container.Resolve<PlayerCommands>();
                var nowPlayingCommands = container.Resolve<NowPlayingCommands>();
                var playlistCommands = container.Resolve<PlaylistCommands>();
                var systemCommands = container.Resolve<SystemCommands>();
                var libraryCommands = container.Resolve<LibraryCommands>();

                // Register commands using individual registration functions
                CommandRegistry.RegisterPlayerCommands(dispatcher, playerCommands);
                CommandRegistry.RegisterNowPlayingCommands(dispatcher, nowPlayingCommands);
                CommandRegistry.RegisterPlaylistCommands(dispatcher, playlistCommands);
                CommandRegistry.RegisterSystemCommands(dispatcher, systemCommands);
                CommandRegistry.RegisterLibraryCommands(dispatcher, libraryCommands);
            });
        }
    }
}
