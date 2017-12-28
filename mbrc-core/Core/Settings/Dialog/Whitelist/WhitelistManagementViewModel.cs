using System.Collections.Generic;
using MusicBeeRemote.Core.Windows.Mvvm;

namespace MusicBeeRemote.Core.Settings.Dialog.Whitelist
{
    public class WhitelistManagementViewModel : ViewModelBase
    {
        public WhitelistManagementViewModel(PersistanceManager persistanceManager)
        {
            Whitelist = persistanceManager.UserSettingsModel.IpAddressList;
        }

        public List<string> Whitelist { get; }

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