using System.Windows.Forms;

namespace MusicBeeRemote.Core.Settings.Dialog.PartyMode
{
    partial class PartyModePanel
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
                _eventSubscription.Dispose();
            }
            
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.clientListGrid = new System.Windows.Forms.DataGridView();
            this.knownClientsLabel = new System.Windows.Forms.Label();
            this.logsLabel = new System.Windows.Forms.Label();
            this.activeCheckbox = new System.Windows.Forms.CheckBox();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.keepLabel = new System.Windows.Forms.Label();
            this.numericUpDown1 = new System.Windows.Forms.NumericUpDown();
            this.clientPermissionsGroupBox = new System.Windows.Forms.GroupBox();
            this.startPlaybackCheckbox = new System.Windows.Forms.CheckBox();
            this.changeMuteCheckBox = new System.Windows.Forms.CheckBox();
            this.stopPlaybackCheckbox = new System.Windows.Forms.CheckBox();
            this.changeRepeatCheckBox = new System.Windows.Forms.CheckBox();
            this.playNextCheckbox = new System.Windows.Forms.CheckBox();
            this.changeShuffleCheckBox = new System.Windows.Forms.CheckBox();
            this.playPreviousCheckBox = new System.Windows.Forms.CheckBox();
            this.changeVolumeCheckbox = new System.Windows.Forms.CheckBox();
            this.addTrackCheckbox = new System.Windows.Forms.CheckBox();
            this.removeTrackCheckbox = new System.Windows.Forms.CheckBox();
            this.logGrid = new System.Windows.Forms.DataGridView();
            ((System.ComponentModel.ISupportInitialize) (this.clientListGrid)).BeginInit();
            ((System.ComponentModel.ISupportInitialize) (this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize) (this.numericUpDown1)).BeginInit();
            this.clientPermissionsGroupBox.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize) (this.logGrid)).BeginInit();
            this.SuspendLayout();
            // 
            // clientListGrid
            // 
            this.clientListGrid.AllowUserToAddRows = false;
            this.clientListGrid.AllowUserToDeleteRows = false;
            this.clientListGrid.Anchor = ((System.Windows.Forms.AnchorStyles) ((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.clientListGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.clientListGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.clientListGrid.Location = new System.Drawing.Point(11, 50);
            this.clientListGrid.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.clientListGrid.Name = "clientListGrid";
            this.clientListGrid.ReadOnly = true;
            this.clientListGrid.RowTemplate.Height = 28;
            this.clientListGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.clientListGrid.Size = new System.Drawing.Size(707, 262);
            this.clientListGrid.TabIndex = 0;
            this.clientListGrid.RowStateChanged += new System.Windows.Forms.DataGridViewRowStateChangedEventHandler(this.ClientListGridRowStateChanged);
            // 
            // knownClientsLabel
            // 
            this.knownClientsLabel.AutoSize = true;
            this.knownClientsLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte) (0)));
            this.knownClientsLabel.Location = new System.Drawing.Point(7, 31);
            this.knownClientsLabel.Name = "knownClientsLabel";
            this.knownClientsLabel.Size = new System.Drawing.Size(109, 17);
            this.knownClientsLabel.TabIndex = 1;
            this.knownClientsLabel.Text = "Known Clients";
            // 
            // logsLabel
            // 
            this.logsLabel.AutoSize = true;
            this.logsLabel.Location = new System.Drawing.Point(3, 0);
            this.logsLabel.Name = "logsLabel";
            this.logsLabel.Size = new System.Drawing.Size(39, 17);
            this.logsLabel.TabIndex = 5;
            this.logsLabel.Text = "Logs";
            // 
            // activeCheckbox
            // 
            this.activeCheckbox.AutoSize = true;
            this.activeCheckbox.Location = new System.Drawing.Point(11, 10);
            this.activeCheckbox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.activeCheckbox.Name = "activeCheckbox";
            this.activeCheckbox.Size = new System.Drawing.Size(68, 21);
            this.activeCheckbox.TabIndex = 6;
            this.activeCheckbox.Text = "Active";
            this.activeCheckbox.UseVisualStyleBackColor = true;
            this.activeCheckbox.CheckedChanged += new System.EventHandler(this.ActiveCheckboxCheckedChanged);
            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 0);
            this.splitContainer1.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.splitContainer1.Name = "splitContainer1";
            this.splitContainer1.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.keepLabel);
            this.splitContainer1.Panel1.Controls.Add(this.numericUpDown1);
            this.splitContainer1.Panel1.Controls.Add(this.clientPermissionsGroupBox);
            this.splitContainer1.Panel1.Controls.Add(this.clientListGrid);
            this.splitContainer1.Panel1.Controls.Add(this.activeCheckbox);
            this.splitContainer1.Panel1.Controls.Add(this.knownClientsLabel);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.logsLabel);
            this.splitContainer1.Panel2.Controls.Add(this.logGrid);
            this.splitContainer1.Size = new System.Drawing.Size(891, 570);
            this.splitContainer1.SplitterDistance = 314;
            this.splitContainer1.SplitterWidth = 3;
            this.splitContainer1.TabIndex = 7;
            // 
            // keepLabel
            // 
            this.keepLabel.AutoSize = true;
            this.keepLabel.Location = new System.Drawing.Point(580, 13);
            this.keepLabel.Name = "keepLabel";
            this.keepLabel.Size = new System.Drawing.Size(223, 17);
            this.keepLabel.TabIndex = 30;
            this.keepLabel.Text = "Days to keep after last connection";
            // 
            // numericUpDown1
            // 
            this.numericUpDown1.Location = new System.Drawing.Point(808, 11);
            this.numericUpDown1.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.numericUpDown1.Maximum = new decimal(new int[] {30, 0, 0, 0});
            this.numericUpDown1.Name = "numericUpDown1";
            this.numericUpDown1.Size = new System.Drawing.Size(72, 22);
            this.numericUpDown1.TabIndex = 29;
            // 
            // clientPermissionsGroupBox
            // 
            this.clientPermissionsGroupBox.Anchor = ((System.Windows.Forms.AnchorStyles) ((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.clientPermissionsGroupBox.Controls.Add(this.startPlaybackCheckbox);
            this.clientPermissionsGroupBox.Controls.Add(this.changeMuteCheckBox);
            this.clientPermissionsGroupBox.Controls.Add(this.stopPlaybackCheckbox);
            this.clientPermissionsGroupBox.Controls.Add(this.changeRepeatCheckBox);
            this.clientPermissionsGroupBox.Controls.Add(this.playNextCheckbox);
            this.clientPermissionsGroupBox.Controls.Add(this.changeShuffleCheckBox);
            this.clientPermissionsGroupBox.Controls.Add(this.playPreviousCheckBox);
            this.clientPermissionsGroupBox.Controls.Add(this.changeVolumeCheckbox);
            this.clientPermissionsGroupBox.Controls.Add(this.addTrackCheckbox);
            this.clientPermissionsGroupBox.Controls.Add(this.removeTrackCheckbox);
            this.clientPermissionsGroupBox.Location = new System.Drawing.Point(723, 50);
            this.clientPermissionsGroupBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.clientPermissionsGroupBox.Name = "clientPermissionsGroupBox";
            this.clientPermissionsGroupBox.Padding = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.clientPermissionsGroupBox.Size = new System.Drawing.Size(157, 262);
            this.clientPermissionsGroupBox.TabIndex = 28;
            this.clientPermissionsGroupBox.TabStop = false;
            this.clientPermissionsGroupBox.Text = "Permissions";
            // 
            // startPlaybackCheckbox
            // 
            this.startPlaybackCheckbox.AutoSize = true;
            this.startPlaybackCheckbox.Location = new System.Drawing.Point(5, 20);
            this.startPlaybackCheckbox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.startPlaybackCheckbox.Name = "startPlaybackCheckbox";
            this.startPlaybackCheckbox.Size = new System.Drawing.Size(121, 21);
            this.startPlaybackCheckbox.TabIndex = 18;
            this.startPlaybackCheckbox.Text = "Start Playback";
            this.startPlaybackCheckbox.UseVisualStyleBackColor = true;
            this.startPlaybackCheckbox.CheckedChanged += new System.EventHandler(this.StartPlaybackCheckboxCheckedChanged);
            // 
            // changeMuteCheckBox
            // 
            this.changeMuteCheckBox.AutoSize = true;
            this.changeMuteCheckBox.Location = new System.Drawing.Point(5, 236);
            this.changeMuteCheckBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.changeMuteCheckBox.Name = "changeMuteCheckBox";
            this.changeMuteCheckBox.Size = new System.Drawing.Size(61, 21);
            this.changeMuteCheckBox.TabIndex = 27;
            this.changeMuteCheckBox.Text = "Mute";
            this.changeMuteCheckBox.UseVisualStyleBackColor = true;
            this.changeMuteCheckBox.CheckedChanged += new System.EventHandler(this.ChangeMuteCheckBoxCheckedChanged);
            // 
            // stopPlaybackCheckbox
            // 
            this.stopPlaybackCheckbox.AutoSize = true;
            this.stopPlaybackCheckbox.Location = new System.Drawing.Point(5, 44);
            this.stopPlaybackCheckbox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.stopPlaybackCheckbox.Name = "stopPlaybackCheckbox";
            this.stopPlaybackCheckbox.Size = new System.Drawing.Size(120, 21);
            this.stopPlaybackCheckbox.TabIndex = 19;
            this.stopPlaybackCheckbox.Text = "Stop Playback";
            this.stopPlaybackCheckbox.UseVisualStyleBackColor = true;
            this.stopPlaybackCheckbox.CheckedChanged += new System.EventHandler(this.StopPlaybackCheckboxCheckedChanged);
            // 
            // changeRepeatCheckBox
            // 
            this.changeRepeatCheckBox.AutoSize = true;
            this.changeRepeatCheckBox.Location = new System.Drawing.Point(5, 212);
            this.changeRepeatCheckBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.changeRepeatCheckBox.Name = "changeRepeatCheckBox";
            this.changeRepeatCheckBox.Size = new System.Drawing.Size(129, 21);
            this.changeRepeatCheckBox.TabIndex = 26;
            this.changeRepeatCheckBox.Text = "Change Repeat";
            this.changeRepeatCheckBox.UseVisualStyleBackColor = true;
            this.changeRepeatCheckBox.CheckedChanged += new System.EventHandler(this.ChangeRepeatCheckBoxCheckedChanged);
            // 
            // playNextCheckbox
            // 
            this.playNextCheckbox.AutoSize = true;
            this.playNextCheckbox.Location = new System.Drawing.Point(5, 68);
            this.playNextCheckbox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.playNextCheckbox.Name = "playNextCheckbox";
            this.playNextCheckbox.Size = new System.Drawing.Size(89, 21);
            this.playNextCheckbox.TabIndex = 20;
            this.playNextCheckbox.Text = "Play Next";
            this.playNextCheckbox.UseVisualStyleBackColor = true;
            this.playNextCheckbox.CheckedChanged += new System.EventHandler(this.PlayNextCheckboxCheckedChanged);
            // 
            // changeShuffleCheckBox
            // 
            this.changeShuffleCheckBox.AutoSize = true;
            this.changeShuffleCheckBox.Location = new System.Drawing.Point(5, 188);
            this.changeShuffleCheckBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.changeShuffleCheckBox.Name = "changeShuffleCheckBox";
            this.changeShuffleCheckBox.Size = new System.Drawing.Size(127, 21);
            this.changeShuffleCheckBox.TabIndex = 25;
            this.changeShuffleCheckBox.Text = "Change Shuffle";
            this.changeShuffleCheckBox.UseVisualStyleBackColor = true;
            this.changeShuffleCheckBox.CheckedChanged += new System.EventHandler(this.ChangeShuffleCheckBoxCheckedChanged);
            // 
            // playPreviousCheckBox
            // 
            this.playPreviousCheckBox.AutoSize = true;
            this.playPreviousCheckBox.Location = new System.Drawing.Point(5, 92);
            this.playPreviousCheckBox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.playPreviousCheckBox.Name = "playPreviousCheckBox";
            this.playPreviousCheckBox.Size = new System.Drawing.Size(116, 21);
            this.playPreviousCheckBox.TabIndex = 21;
            this.playPreviousCheckBox.Text = "Play Previous";
            this.playPreviousCheckBox.UseVisualStyleBackColor = true;
            this.playPreviousCheckBox.CheckedChanged += new System.EventHandler(this.PlayPreviousCheckBoxCheckedChanged);
            // 
            // changeVolumeCheckbox
            // 
            this.changeVolumeCheckbox.AutoSize = true;
            this.changeVolumeCheckbox.Location = new System.Drawing.Point(5, 164);
            this.changeVolumeCheckbox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.changeVolumeCheckbox.Name = "changeVolumeCheckbox";
            this.changeVolumeCheckbox.Size = new System.Drawing.Size(130, 21);
            this.changeVolumeCheckbox.TabIndex = 24;
            this.changeVolumeCheckbox.Text = "Change Volume";
            this.changeVolumeCheckbox.UseVisualStyleBackColor = true;
            this.changeVolumeCheckbox.CheckedChanged += new System.EventHandler(this.ChangeVolumeCheckboxCheckedChanged);
            // 
            // addTrackCheckbox
            // 
            this.addTrackCheckbox.AutoSize = true;
            this.addTrackCheckbox.Location = new System.Drawing.Point(5, 116);
            this.addTrackCheckbox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.addTrackCheckbox.Name = "addTrackCheckbox";
            this.addTrackCheckbox.Size = new System.Drawing.Size(95, 21);
            this.addTrackCheckbox.TabIndex = 22;
            this.addTrackCheckbox.Text = "Add Track";
            this.addTrackCheckbox.UseVisualStyleBackColor = true;
            this.addTrackCheckbox.CheckedChanged += new System.EventHandler(this.AddTrackCheckboxCheckedChanged);
            // 
            // removeTrackCheckbox
            // 
            this.removeTrackCheckbox.AutoSize = true;
            this.removeTrackCheckbox.Location = new System.Drawing.Point(5, 140);
            this.removeTrackCheckbox.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.removeTrackCheckbox.Name = "removeTrackCheckbox";
            this.removeTrackCheckbox.Size = new System.Drawing.Size(118, 21);
            this.removeTrackCheckbox.TabIndex = 23;
            this.removeTrackCheckbox.Text = "RemoveTrack";
            this.removeTrackCheckbox.UseVisualStyleBackColor = true;
            this.removeTrackCheckbox.CheckedChanged += new System.EventHandler(this.RemoveTrackCheckboxCheckedChanged);
            // 
            // logGrid
            // 
            this.logGrid.AllowUserToAddRows = false;
            this.logGrid.AllowUserToDeleteRows = false;
            this.logGrid.Anchor = ((System.Windows.Forms.AnchorStyles) ((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right)));
            this.logGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.logGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.logGrid.Location = new System.Drawing.Point(6, 18);
            this.logGrid.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.logGrid.Name = "logGrid";
            this.logGrid.ReadOnly = true;
            this.logGrid.RowTemplate.Height = 28;
            this.logGrid.Size = new System.Drawing.Size(874, 227);
            this.logGrid.TabIndex = 4;
            // 
            // PartyModePanel
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(891, 570);
            this.Controls.Add(this.splitContainer1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Margin = new System.Windows.Forms.Padding(3, 2, 3, 2);
            this.Name = "PartyModePanel";
            this.Text = "MusicBeeRemote: Party Mode Permissions";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.PartyModePanel_FormClosing);
            this.Load += new System.EventHandler(this.PartyModePanelLoad);
            ((System.ComponentModel.ISupportInitialize) (this.clientListGrid)).EndInit();
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel1.PerformLayout();
            this.splitContainer1.Panel2.ResumeLayout(false);
            this.splitContainer1.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize) (this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize) (this.numericUpDown1)).EndInit();
            this.clientPermissionsGroupBox.ResumeLayout(false);
            this.clientPermissionsGroupBox.PerformLayout();
            ((System.ComponentModel.ISupportInitialize) (this.logGrid)).EndInit();
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.CheckBox activeCheckbox;
        private System.Windows.Forms.CheckBox addTrackCheckbox;
        private System.Windows.Forms.CheckBox changeMuteCheckBox;
        private System.Windows.Forms.CheckBox changeRepeatCheckBox;
        private System.Windows.Forms.CheckBox changeShuffleCheckBox;
        private System.Windows.Forms.CheckBox changeVolumeCheckbox;
        private System.Windows.Forms.DataGridView clientListGrid;
        private System.Windows.Forms.GroupBox clientPermissionsGroupBox;
        private System.Windows.Forms.Label keepLabel;
        private System.Windows.Forms.Label knownClientsLabel;
        private System.Windows.Forms.DataGridView logGrid;
        private System.Windows.Forms.Label logsLabel;
        private System.Windows.Forms.NumericUpDown numericUpDown1;
        private System.Windows.Forms.CheckBox playNextCheckbox;
        private System.Windows.Forms.CheckBox playPreviousCheckBox;
        private System.Windows.Forms.CheckBox removeTrackCheckbox;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.CheckBox startPlaybackCheckbox;
        private System.Windows.Forms.CheckBox stopPlaybackCheckbox;

        #endregion
    }
}
