using System;
using System.Windows.Forms;
using MusicBeeRemote.Core.Commands;
using MusicBeeRemote.Core.Settings.Dialog;
using MusicBeeRemote.PartyMode.Core.Helper;
using MusicBeeRemote.PartyMode.Core.Model;
using MusicBeeRemote.PartyMode.Core.ViewModel;
using TinyMessenger;

namespace MusicBeeRemote.PartyMode.Core
{
    public partial class PartyModePanel : Form
    {
        private readonly PartyModeViewModel _viewModel;
        private readonly ITinyMessengerHub _hub;
        private readonly CycledList<PartyModeLog> _logs;
        private TinyMessageSubscriptionToken _eventSubscription;

        public PartyModePanel(PartyModeViewModel viewModel, ITinyMessengerHub hub)
        {
            _viewModel = viewModel;
            _hub = hub;
            _logs = new CycledList<PartyModeLog>(10000);
            InitializeComponent();
            clientListGrid.DataSource = _viewModel.KnownClients;
            _logs.AddRange(_viewModel.GetLogs());
            logGrid.DataSource = _logs;
            activeCheckbox.SetChecked(_viewModel.IsActive);
            _eventSubscription = _hub.Subscribe<CommandProcessedEvent>(msg =>
            {
                _logs.Add(msg.Log);
                logGrid.FirstDisplayedScrollingRowIndex = logGrid.RowCount - 1;
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
        }

        private void PartyModePanel_FormClosing(object sender, FormClosingEventArgs e)
        {
            _hub.Unsubscribe<CommandProcessedEvent>(_eventSubscription);
        }
    }
}