using System;
using System.Windows.Input;

namespace MusicBeeRemote.Core.Settings.Dialog.Commands
{
    public class RemoveAddressFromWhiteListCommand
    {
        private readonly AddressWhitelistViewModel _viewModel;

        public RemoveAddressFromWhiteListCommand(AddressWhitelistViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        public void Execute(object parameter)
        {
            _viewModel.RemoveAddress(parameter.ToString());
        }
    }
}
