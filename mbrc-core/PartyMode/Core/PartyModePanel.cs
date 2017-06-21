using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using MusicBeeRemote.Core.Network;
using MusicBeeRemote.Core.Settings.Dialog;
using MusicBeeRemote.PartyMode.Core.Model;
using MusicBeeRemote.PartyMode.Core.ViewModel;

namespace MusicBeeRemote.PartyMode.Core
{
    public partial class PartyModePanel : Form
    {
        private readonly PartyModeViewModel _viewModel;

        public PartyModePanel(PartyModeViewModel viewModel)
        {
            _viewModel = viewModel;
            InitializeComponent();           
            clientListGrid.DataSource = _viewModel.KnownClients;
            
            logGrid.DataSource = _viewModel.Logs;
            checkedListBox1.DataSource = _viewModel.Permissions;

            activeCheckbox.SetChecked(_viewModel.IsActive);
        }

        private void activeCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            _viewModel.IsActive = activeCheckbox.Checked;
        }

        private void clientListGrid_RowStateChanged(object sender, DataGridViewRowStateChangedEventArgs e)
        {
            if (e.StateChanged != DataGridViewElementStates.Selected)
            {
                return;
            }            
        }
    }
}