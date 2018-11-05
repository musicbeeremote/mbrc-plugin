using System;
using System.ComponentModel;
using System.Windows.Forms;
using MusicBeeRemote.Core.Commands;
using MusicBeeRemote.Core.Commands.Logs;
using MusicBeeRemote.Core.Network;
using TinyMessenger;

namespace MusicBeeRemote.Core.Settings.Dialog.PartyModePanel
{
    public partial class PartyModePanel : Form
    {
        private readonly PartyModeViewModel _viewModel;
        private readonly ITinyMessengerHub _hub;
        private TinyMessageSubscriptionToken _eventSubscription;

        public PartyModePanel(PartyModeViewModel viewModel, ITinyMessengerHub hub)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _hub = hub;
            clientListGrid.DataSource = new BindingList<RemoteClient>(_viewModel.KnownClients);
            var logData = new BindingList<ExecutionLog>(_viewModel.GetLogs());
            logGrid.DataSource = logData;
            activeCheckbox.SetChecked(_viewModel.IsActive);
            _eventSubscription = _hub.Subscribe<ClientManager.ActionLoggedEvent>(msg =>
            {
                logData.Add(msg.Log);
                logGrid.FirstDisplayedScrollingRowIndex = logGrid.RowCount - 1;
                logGrid.Refresh();
            });
        }

        private void ActiveCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            _viewModel.IsActive = activeCheckbox.Checked;
        }

        private void ClientListGrid_RowStateChanged(object sender, DataGridViewRowStateChangedEventArgs e)
        {
            if (e.StateChanged != DataGridViewElementStates.Selected)
            {
                return;
            }

            if (!e.Row.Selected) return;

            var rowIndex = e.Row.Index;
            _viewModel.SelectClient(rowIndex);
            startPlaybackCheckbox.SetChecked(_viewModel.SelectedClientHasPermission(CommandPermissions.StartPlayback));
            stopPlaybackCheckbox.SetChecked(_viewModel.SelectedClientHasPermission(CommandPermissions.StopPlayback));
            playNextCheckbox.SetChecked(_viewModel.SelectedClientHasPermission(CommandPermissions.PlayNext));
            playPreviousCheckBox.SetChecked(_viewModel.SelectedClientHasPermission(CommandPermissions.PlayPrevious));
            addTrackCheckbox.SetChecked(_viewModel.SelectedClientHasPermission(CommandPermissions.AddTrack));
            removeTrackCheckbox.SetChecked(_viewModel.SelectedClientHasPermission(CommandPermissions.RemoveTrack));
            changeVolumeCheckbox.SetChecked(_viewModel.SelectedClientHasPermission(CommandPermissions.ChangeVolume));
            changeShuffleCheckBox.SetChecked(_viewModel.SelectedClientHasPermission(CommandPermissions.ChangeShuffle));
            changeRepeatCheckBox.SetChecked(_viewModel.SelectedClientHasPermission(CommandPermissions.ChangeRepeat));
            changeMuteCheckBox.SetChecked(_viewModel.SelectedClientHasPermission(CommandPermissions.CanMute));
        }

        private void StopPlaybackCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.StopPlayback, stopPlaybackCheckbox.Checked);
        }

        private void RemoveTrackCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.RemoveTrack, removeTrackCheckbox.Checked);
        }

        private void StartPlaybackCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.StartPlayback, startPlaybackCheckbox.Checked);
        }

        private void PlayNextCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.PlayNext, playNextCheckbox.Checked);
        }

        private void PlayPreviousCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.PlayPrevious, playPreviousCheckBox.Checked);
        }

        private void AddTrackCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.AddTrack, addTrackCheckbox.Checked);
        }

        private void ChangeVolumeCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.ChangeVolume, changeVolumeCheckbox.Checked);
        }

        private void ChangeShuffleCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.ChangeShuffle, changeShuffleCheckBox.Checked);
        }

        private void ChangeRepeatCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.ChangeRepeat, changeRepeatCheckBox.Checked);
        }

        private void ChangeMuteCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.CanMute, changeMuteCheckBox.Checked);
        }

        private void PartyModePanel_Load(object sender, EventArgs e)
        {
            _viewModel.ClientDataUpdated += (o, args) =>
            {
                clientListGrid.DataSource = new BindingList<RemoteClient>(_viewModel.KnownClients);
            };
        }

        private void PartyModePanel_FormClosing(object sender, FormClosingEventArgs e)
        {            
            _hub.Unsubscribe<ClientManager.ActionLoggedEvent>(_eventSubscription);
        }
    }
}