using System;
using System.Windows.Input;

namespace MusicBeeRemote.Core.Settings.Dialog.Commands
{
    public class AddAddressToWhiteListCommand 
    {
        private readonly AddressWhitelistViewModel _viewModel;

        public AddAddressToWhiteListCommand(AddressWhitelistViewModel viewModel)
        {
            _viewModel = viewModel;
        }

        public void Execute(object parameter)
        {
            _viewModel.AddAddress(parameter.ToString());
        }        
    }
}
