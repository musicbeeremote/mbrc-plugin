using System;
using System.ComponentModel;
using System.Windows.Forms;
using MusicBeeRemote.Core.Commands;
using MusicBeeRemote.Core.Commands.Logs;
using MusicBeeRemote.Core.Network;
using TinyMessenger;

namespace MusicBeeRemote.Core.Settings.Dialog.PartyMode
{
    /// <summary>
    /// The Party Mode control panel.
    /// </summary>
    public partial class PartyModePanel : Form
    {
        private readonly PartyModeViewModel _viewModel;
        private readonly ITinyMessengerHub _hub;
        private readonly TinyMessageSubscriptionToken _eventSubscription;

        public PartyModePanel(PartyModeViewModel viewModel, ITinyMessengerHub hub)
        {
            InitializeComponent();
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            clientListGrid.DataSource = new BindingList<RemoteClient>(_viewModel.KnownClients);
            var logData = new BindingList<ExecutionLog>(_viewModel.GetLogs());
            logGrid.DataSource = logData;
            activeCheckbox.SetChecked(_viewModel.IsActive);
            _eventSubscription = _hub.Subscribe<ActionLoggedEvent>(msg =>
            {
                logData.Add(msg.Log);
                logGrid.FirstDisplayedScrollingRowIndex = logGrid.RowCount - 1;
                logGrid.Refresh();
            });
        }

        private void ActiveCheckboxCheckedChanged(object sender, EventArgs e)
        {
            _viewModel.IsActive = activeCheckbox.Checked;
        }

        private void ClientListGridRowStateChanged(object sender, DataGridViewRowStateChangedEventArgs e)
        {
            if (e.StateChanged != DataGridViewElementStates.Selected)
            {
                return;
            }

            if (!e.Row.Selected)
            {
                return;
            }

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

        private void StopPlaybackCheckboxCheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.StopPlayback, stopPlaybackCheckbox.Checked);
        }

        private void RemoveTrackCheckboxCheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.RemoveTrack, removeTrackCheckbox.Checked);
        }

        private void StartPlaybackCheckboxCheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.StartPlayback, startPlaybackCheckbox.Checked);
        }

        private void PlayNextCheckboxCheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.PlayNext, playNextCheckbox.Checked);
        }

        private void PlayPreviousCheckBoxCheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.PlayPrevious, playPreviousCheckBox.Checked);
        }

        private void AddTrackCheckboxCheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.AddTrack, addTrackCheckbox.Checked);
        }

        private void ChangeVolumeCheckboxCheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.ChangeVolume, changeVolumeCheckbox.Checked);
        }

        private void ChangeShuffleCheckBoxCheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.ChangeShuffle, changeShuffleCheckBox.Checked);
        }

        private void ChangeRepeatCheckBoxCheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.ChangeRepeat, changeRepeatCheckBox.Checked);
        }

        private void ChangeMuteCheckBoxCheckedChanged(object sender, EventArgs e)
        {
            _viewModel.UpdateSelectedClientPermissions(CommandPermissions.CanMute, changeMuteCheckBox.Checked);
        }

        private void PartyModePanelLoad(object sender, EventArgs e)
        {
            _viewModel.ClientDataUpdated += (o, args) =>
            {
                clientListGrid.DataSource = new BindingList<RemoteClient>(_viewModel.KnownClients);
            };
        }

        private void PartyModePanel_FormClosing(object sender, FormClosingEventArgs e)
        {
            _hub.Unsubscribe<ActionLoggedEvent>(_eventSubscription);
        }
    }
}
