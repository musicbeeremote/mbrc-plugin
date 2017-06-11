using MusicBeeRemote.Core.Windows.Mvvm;

namespace MusicBeeRemote.Core.Settings.Dialog.Range
{
    public class RangeManagementViewModel : ViewModelBase
    {
        private readonly UserSettingsModel _userSettingsModel;

        public RangeManagementViewModel(PersistanceManager persistanceManager)
        {
            _userSettingsModel = persistanceManager.UserSettingsModel;
        }

        public uint LastOctetMax
        {
            get => _userSettingsModel.LastOctetMax;
            set
            {
                _userSettingsModel.LastOctetMax = value;
                OnPropertyChanged(nameof(LastOctetMax));
            }
        }

        public string BaseIp
        {
            get => _userSettingsModel.BaseIp;
            set
            {
                _userSettingsModel.BaseIp = value;
                OnPropertyChanged(nameof(BaseIp));
            }
        }
    }
}