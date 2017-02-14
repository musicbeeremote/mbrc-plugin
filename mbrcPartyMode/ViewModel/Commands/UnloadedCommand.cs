using System;
using System.Windows.Input;

namespace mbrcPartyMode.ViewModel.Commands
{
    public class UnloadedCommand : ICommand
    {
        private readonly Action _doAction;

        public UnloadedCommand(Action doAction)
        {
            _doAction = doAction;
        }

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public event EventHandler CanExecuteChanged;

        public void Execute(object parameter)
        {
            _doAction();
        }
    }
}