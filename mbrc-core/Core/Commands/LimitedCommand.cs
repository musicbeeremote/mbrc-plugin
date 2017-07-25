using MusicBeeRemote.Core.Events;

namespace MusicBeeRemote.Core.Commands
{
    /// <summary>
    /// A Command that has limitations. Thus functionality can be restricted if the client
    /// doesn't have the necessary permissions to run the command.
    /// </summary>
    public abstract class LimitedCommand : ICommand
    {
        public abstract void Execute(IEvent @event);

        /// <summary>
        /// Permissions required by the command in order to run.
        /// </summary>
        /// <returns>The required command permissions.</returns>
        public abstract CommandPermissions GetPermissions();

        /// <summary>
        /// Takes as input the event data and the client permissions and executes only the commands that actually
        /// match. The commands the support partial execution should override the method
        /// </summary>
        /// <param name="event">The event with the data payload.</param>
        /// <param name="permissions">The client permissions.</param>
        /// <returns>The execution status of the command.</returns>
        public ExecutionStatus Execute(IEvent @event, CommandPermissions permissions)
        {
            if (!GetPermissions().HasFlag(permissions))
            {
                return ExecutionStatus.Denied;
            }

            Execute(@event);
            return ExecutionStatus.Executed;
        }
    }
}