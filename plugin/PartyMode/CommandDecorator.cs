using MusicBeePlugin.AndroidRemote.Interfaces;

namespace MusicBeePlugin.PartyMode
{
    public abstract class CommandDecorator : ICommand
    {
        private readonly ICommand _command;

        protected CommandDecorator(ICommand cmd)
        {
            _command = cmd;
        }

        public virtual void Execute(IEvent eEvent)
        {
            _command.Execute(eEvent);
        }
    }
}