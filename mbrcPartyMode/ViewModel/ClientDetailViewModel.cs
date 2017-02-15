using mbrcPartyMode.Helper;
using System.Windows;

namespace mbrcPartyMode.ViewModel
{
    public class ClientDetailViewModel : ModelBase
    {
        private readonly ClientAdress clientAddress;


        public ClientDetailViewModel(ClientAdress adr)
        {
            this.clientAddress = adr;
            OnPropertyChanged(nameof(IsVisible));
        }

        public bool CanAddToPlayList
        {
            get
            {
                if (clientAddress != null)
                {
                    return clientAddress.CanAddToPlayList;
                }
                return true;
            }

            set
            {
                if (clientAddress != null)
                {
                    clientAddress.CanAddToPlayList = value;
                    OnPropertyChanged(nameof(CanAddToPlayList));
                }
            }
        }

        public bool CanSkipBackwards
        {
            get
            {
                if (clientAddress != null)
                {
                    return clientAddress.CanSkipBackwards;
                }
                return true;
            }

            set
            {
                clientAddress.CanSkipBackwards = value;
                OnPropertyChanged(nameof(CanSkipBackwards));
            }
        }

        public bool CanDeleteFromPlayList
        {
            get
            {
                if (clientAddress != null)
                {
                    return clientAddress.CanDeleteFromPlayList;
                }
                return true;
            }

            set
            {
                clientAddress.CanDeleteFromPlayList = value;
                OnPropertyChanged(nameof(CanDeleteFromPlayList));
            }
        }

        public bool CanSkipForwards
        {
            get
            {
                if (clientAddress != null)
                {
                    return clientAddress.CanSkipForwards;
                }
                return true;
            }

            set
            {
                clientAddress.CanSkipForwards = value;
                OnPropertyChanged(nameof(this.CanSkipForwards));
            }
        }

        public bool CanStartStopPlayer
        {
            get
            {
                if (clientAddress != null)
                {
                    return clientAddress.CanStartStopPlayer;
                }
                return true;
            }

            set
            {
                clientAddress.CanStartStopPlayer = value;
                OnPropertyChanged(nameof(CanStartStopPlayer));
            }
        }

        public bool CanVolumeUpDown
        {
            get
            {
                if (clientAddress != null)
                {
                    return clientAddress.CanVolumeUpDown;
                }
                return true;
            }
            set
            {
                clientAddress.CanVolumeUpDown = value;
                OnPropertyChanged(nameof(CanVolumeUpDown));
            }
        }

        public bool CanShuffle
        {
            get
            {
                if (clientAddress != null)
                {
                    return clientAddress.CanShuffle;
                }
                return true;
            }
            set
            {
                clientAddress.CanShuffle = value;
                OnPropertyChanged(nameof(CanShuffle));
            }
        }

        public bool CanReplay
        {
            get
            {
                if (clientAddress != null)
                {
                    return clientAddress.CanReplay;
                }
                return true;
            }
            set
            {
                clientAddress.CanReplay = value;
                OnPropertyChanged(nameof(CanReplay));
            }
        }
        
        public bool CanMute
        {
            get
            {
                if (clientAddress != null)
                {
                    return clientAddress.CanMute;
                }
                return true;
            }
            set
            {
                clientAddress.CanMute = value;
                OnPropertyChanged(nameof(CanMute));
            }
        }
        public Visibility IsVisible
        {
            get
            {
                if (clientAddress == null)
                {
                    return Visibility.Hidden;
                }
                else
                {
                    return Visibility.Visible;
                }
            }
           
        }

        #region text

        public string CommandOverViewText
        {
            get { return "command restrictions"; }
        }

        #endregion text
    }
}