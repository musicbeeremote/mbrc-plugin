using System.Windows;
using MusicBeeRemote.Core.Commands;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Windows.Mvvm;

namespace MusicBeeRemote.PartyMode.Core.ViewModel
{
    public sealed class ClientDetailViewModel : ViewModelBase
    {
        private readonly RemoteClient _connectedClient;

        public ClientDetailViewModel(RemoteClient client)
        {
            _connectedClient = client;
            OnPropertyChanged(nameof(IsVisible));
        }

        public bool CanAddToPlayList
        {
            get => _connectedClient == null || _connectedClient.HasPermission(CommandPermissions.AddTrack);
            set
            {
                if (_connectedClient == null) return;
                _connectedClient.SetPermission(CommandPermissions.AddTrack, value);
                OnPropertyChanged(nameof(CanAddToPlayList));
            }
        }

        public bool CanSkipBackwards
        {
            get => _connectedClient == null || _connectedClient.HasPermission(CommandPermissions.PlayPrevious);
            set
            {
                _connectedClient.SetPermission(CommandPermissions.PlayPrevious, value);
                OnPropertyChanged(nameof(CanSkipBackwards));
            }
        }

        public bool CanDeleteFromPlayList
        {
            get => _connectedClient == null || _connectedClient.HasPermission(CommandPermissions.RemoveTrack);
            set
            {
                _connectedClient.SetPermission(CommandPermissions.RemoveTrack, value);
                OnPropertyChanged(nameof(CanDeleteFromPlayList));
            }
        }

        public bool CanSkipForwards
        {
            get => _connectedClient == null || _connectedClient.HasPermission(CommandPermissions.PlayNext);
            set
            {
                _connectedClient.SetPermission(CommandPermissions.PlayNext, value);
                OnPropertyChanged(nameof(CanSkipForwards));
            }
        }

        public bool CanStartStopPlayer
        {
            get => _connectedClient == null || _connectedClient.HasPermission(CommandPermissions.StopPlayback);
            set
            {
                _connectedClient.SetPermission(CommandPermissions.StopPlayback, value);
                OnPropertyChanged(nameof(CanStartStopPlayer));
            }
        }

        public bool CanVolumeUpDown
        {
            get => _connectedClient == null || _connectedClient.HasPermission(CommandPermissions.ChangeVolume);
            set
            {
                _connectedClient.SetPermission(CommandPermissions.ChangeVolume, value);
                OnPropertyChanged(nameof(CanVolumeUpDown));
            }
        }

        public bool CanShuffle
        {
            get => _connectedClient == null || _connectedClient.HasPermission(CommandPermissions.ChangeShuffle);
            set
            {
                _connectedClient.SetPermission(CommandPermissions.ChangeShuffle, value);
                OnPropertyChanged(nameof(CanShuffle));
            }
        }

        public bool CanReplay
        {
            get => _connectedClient == null || _connectedClient.HasPermission(CommandPermissions.ChangeRepeat);
            set
            {
                _connectedClient.SetPermission(CommandPermissions.ChangeRepeat, value);
                OnPropertyChanged(nameof(CanReplay));
            }
        }

        public bool CanMute
        {
            get => _connectedClient == null || _connectedClient.HasPermission(CommandPermissions.CanMute);
            set
            {
                _connectedClient.SetPermission(CommandPermissions.CanMute, value);
                OnPropertyChanged(nameof(CanMute));
            }
        }

        public Visibility IsVisible => _connectedClient == null ? Visibility.Hidden : Visibility.Visible;

        #region text

        public string CommandOverViewText => "Client Restrictions";

        #endregion text
    }
}