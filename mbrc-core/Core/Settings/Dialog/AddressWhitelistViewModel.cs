using System.Windows.Input;
using MusicBeeRemote.Core.Settings.Dialog.Commands;
using MusicBeeRemote.Core.Windows.Mvvm;

namespace MusicBeeRemote.Core.Settings.Dialog
{
    public class AddressWhitelistViewModel : ViewModelBase
    {
        private readonly AddAddressToWhiteListCommand _addAddressCommand;
        private readonly RemoveAddressFromWhiteListCommand _removeAddressCommand;

        public AddressWhitelistViewModel(AddAddressToWhiteListCommand addAddressCommand,
            RemoveAddressFromWhiteListCommand removeAddressCommand)
        {
            _addAddressCommand = addAddressCommand;
            _removeAddressCommand = removeAddressCommand;
        }

        public ICommand AddAddressCommand => _addAddressCommand;

        public ICommand RemoveAddressCommand => _removeAddressCommand;

        public string IpAddress { get; set; }
    }
}