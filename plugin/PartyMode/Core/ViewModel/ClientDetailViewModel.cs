using System.Windows;
using MusicBeePlugin.AndroidRemote.Commands;
using MusicBeePlugin.AndroidRemote.Networking;
using static MusicBeePlugin.AndroidRemote.Commands.CommandPermissions;

namespace MusicBeePlugin.PartyMode.Core.ViewModel
{
    public sealed class ClientDetailViewModel : ModelBase
    {
        private readonly RemoteClient _connectedClient;

        public ClientDetailViewModel(RemoteClient client)
        {
            _connectedClient = client;
            OnPropertyChanged(nameof(IsVisible));
        }

        public bool CanAddToPlayList
        {
            get { return _connectedClient == null || _connectedClient.HasPermission(AddTrack); }
            set
            {
                if (_connectedClient == null) return;
                _connectedClient.SetPermission(AddTrack, value);
                OnPropertyChanged(nameof(CanAddToPlayList));
            }
        }

        public bool CanSkipBackwards
        {
            get { return _connectedClient == null || _connectedClient.HasPermission(PlayPrevious); }
            set
            {
                _connectedClient.SetPermission(PlayPrevious, value);
                OnPropertyChanged(nameof(CanSkipBackwards));
            }
        }

        public bool CanDeleteFromPlayList
        {
            get { return _connectedClient == null || _connectedClient.HasPermission(RemoveTrack); }
            set
            {
                _connectedClient.SetPermission(RemoveTrack, value);
                OnPropertyChanged(nameof(CanDeleteFromPlayList));
            }
        }

        public bool CanSkipForwards
        {
            get { return _connectedClient == null || _connectedClient.HasPermission(PlayNext); }
            set
            {
                _connectedClient.SetPermission(PlayNext, value);
                OnPropertyChanged(nameof(CanSkipForwards));
            }
        }

        public bool CanStartStopPlayer
        {
            get { return _connectedClient == null || _connectedClient.HasPermission(StopPlayback); }
            set
            {
                _connectedClient.SetPermission(StopPlayback, value);
                OnPropertyChanged(nameof(CanStartStopPlayer));
            }
        }

        public bool CanVolumeUpDown
        {
            get { return _connectedClient == null || _connectedClient.HasPermission(ChangeVolume); }
            set
            {
                _connectedClient.SetPermission(ChangeVolume, value);
                OnPropertyChanged(nameof(CanVolumeUpDown));
            }
        }

        public bool CanShuffle
        {
            get { return _connectedClient == null || _connectedClient.HasPermission(ChangeShuffle); }
            set
            {
                _connectedClient.SetPermission(ChangeShuffle, value);
                OnPropertyChanged(nameof(CanShuffle));
            }
        }

        public bool CanReplay
        {
            get { return _connectedClient == null || _connectedClient.HasPermission(ChangeRepeat); }
            set
            {
                _connectedClient.SetPermission(ChangeRepeat, value);
                OnPropertyChanged(nameof(CanReplay));
            }
        }

        public bool CanMute
        {
            get { return _connectedClient == null || _connectedClient.HasPermission(CommandPermissions.CanMute); }
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