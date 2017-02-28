using mbrcPartyMode.Helper;
using System.Windows;

namespace mbrcPartyMode.ViewModel
{
    public sealed class ClientDetailViewModel : ModelBase
    {
        private readonly ClientAdress _clientAddress;


        public ClientDetailViewModel(ClientAdress adr)
        {
            _clientAddress = adr;
            OnPropertyChanged(nameof(IsVisible));
        }

        public bool CanAddToPlayList
        {
            get { return _clientAddress == null || _clientAddress.CanAddToPlayList; }

            set
            {
                if (_clientAddress == null) return;
                _clientAddress.CanAddToPlayList = value;
                OnPropertyChanged(nameof(CanAddToPlayList));
            }
        }

        public bool CanSkipBackwards
        {
            get { return _clientAddress == null || _clientAddress.CanSkipBackwards; }
            set
            {
                _clientAddress.CanSkipBackwards = value;
                OnPropertyChanged(nameof(CanSkipBackwards));
            }
        }

        public bool CanDeleteFromPlayList
        {
            get { return _clientAddress == null || _clientAddress.CanDeleteFromPlayList; }
            set
            {
                _clientAddress.CanDeleteFromPlayList = value;
                OnPropertyChanged(nameof(CanDeleteFromPlayList));
            }
        }

        public bool CanSkipForwards
        {
            get { return _clientAddress == null || _clientAddress.CanSkipForwards; }
            set
            {
                _clientAddress.CanSkipForwards = value;
                OnPropertyChanged(nameof(CanSkipForwards));
            }
        }

        public bool CanStartStopPlayer
        {
            get { return _clientAddress == null || _clientAddress.CanStartStopPlayer; }
            set
            {
                _clientAddress.CanStartStopPlayer = value;
                OnPropertyChanged(nameof(CanStartStopPlayer));
            }
        }

        public bool CanVolumeUpDown
        {
            get { return _clientAddress == null || _clientAddress.CanVolumeUpDown; }
            set
            {
                _clientAddress.CanVolumeUpDown = value;
                OnPropertyChanged(nameof(CanVolumeUpDown));
            }
        }

        public bool CanShuffle
        {
            get { return _clientAddress == null || _clientAddress.CanShuffle; }
            set
            {
                _clientAddress.CanShuffle = value;
                OnPropertyChanged(nameof(CanShuffle));
            }
        }

        public bool CanReplay
        {
            get { return _clientAddress == null || _clientAddress.CanReplay; }
            set
            {
                _clientAddress.CanReplay = value;
                OnPropertyChanged(nameof(CanReplay));
            }
        }

        public bool CanMute
        {
            get { return _clientAddress == null || _clientAddress.CanMute; }
            set
            {
                _clientAddress.CanMute = value;
                OnPropertyChanged(nameof(CanMute));
            }
        }

        public Visibility IsVisible => _clientAddress == null ? Visibility.Hidden : Visibility.Visible;

        #region text

        public string CommandOverViewText => "command restrictions";

        #endregion text
    }
}