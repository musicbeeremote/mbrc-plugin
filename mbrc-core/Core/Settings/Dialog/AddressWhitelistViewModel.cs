using System.Collections.Generic;
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

        public AddAddressToWhiteListCommand AddAddressCommand => _addAddressCommand;

        public RemoveAddressFromWhiteListCommand RemoveAddressCommand => _removeAddressCommand;

        public string IpAddress { get; set; }

        public List<string> Whitelist { get; } = new List<string>();

        public void AddAddress(string address)
        {
            Whitelist.Add(address);
            OnPropertyChanged(nameof(Whitelist));
        }

        public void RemoveAddress(string address)
        {
            Whitelist.Remove(address);
            OnPropertyChanged(nameof(Whitelist));
        }
    }
}