using System.Collections.Generic;
using System.Windows.Input;
using MusicBeeRemote.Core.Settings.Dialog.Commands;
using MusicBeeRemote.Core.Windows.Mvvm;

namespace MusicBeeRemote.Core.Settings.Dialog
{
    public class WhitelistManagementViewModel : ViewModelBase
    {
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