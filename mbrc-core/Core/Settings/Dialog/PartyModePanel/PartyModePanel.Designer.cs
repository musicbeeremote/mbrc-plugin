namespace MusicBeeRemote.Core.Settings.Dialog.PartyModePanel
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
            this.numericUpDown1 = new System.Windows.Forms.NumericUpDown();
            ((System.ComponentModel.ISupportInitialize)(this.clientListGrid)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.clientPermissionsGroupBox.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.logGrid)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).BeginInit();
            this.SuspendLayout();
            // 
            // clientListGrid
            // 
            this.clientListGrid.AllowUserToAddRows = false;
            this.clientListGrid.AllowUserToDeleteRows = false;
            this.clientListGrid.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.clientListGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.clientListGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.clientListGrid.Location = new System.Drawing.Point(12, 62);
            this.clientListGrid.Name = "clientListGrid";
            this.clientListGrid.ReadOnly = true;
            this.clientListGrid.RowTemplate.Height = 28;
            this.clientListGrid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
            this.clientListGrid.Size = new System.Drawing.Size(795, 328);
            this.clientListGrid.TabIndex = 0;
            this.clientListGrid.RowStateChanged += new System.Windows.Forms.DataGridViewRowStateChangedEventHandler(this.ClientListGrid_RowStateChanged);
            // 
            // knownClientsLabel
            // 
            this.knownClientsLabel.AutoSize = true;
            this.knownClientsLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.knownClientsLabel.Location = new System.Drawing.Point(8, 39);
            this.knownClientsLabel.Name = "knownClientsLabel";
            this.knownClientsLabel.Size = new System.Drawing.Size(122, 20);
            this.knownClientsLabel.TabIndex = 1;
            this.knownClientsLabel.Text = "Known Clients";
            // 
            // logsLabel
            // 
            this.logsLabel.AutoSize = true;
            this.logsLabel.Location = new System.Drawing.Point(3, 0);
            this.logsLabel.Name = "logsLabel";
            this.logsLabel.Size = new System.Drawing.Size(44, 20);
            this.logsLabel.TabIndex = 5;
            this.logsLabel.Text = "Logs";
            // 
            // activeCheckbox
            // 
            this.activeCheckbox.AutoSize = true;
            this.activeCheckbox.Location = new System.Drawing.Point(12, 12);
            this.activeCheckbox.Name = "activeCheckbox";
            this.activeCheckbox.Size = new System.Drawing.Size(78, 24);
            this.activeCheckbox.TabIndex = 6;
            this.activeCheckbox.Text = "Active";
            this.activeCheckbox.UseVisualStyleBackColor = true;
            this.activeCheckbox.CheckedChanged += new System.EventHandler(this.ActiveCheckbox_CheckedChanged);
            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 0);
            this.splitContainer1.Name = "splitContainer1";
            this.splitContainer1.Orientation = System.Windows.Forms.Orientation.Horizontal;
            // 
            // splitContainer1.Panel1
            // 
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
            this.splitContainer1.Size = new System.Drawing.Size(1002, 712);
            this.splitContainer1.SplitterDistance = 393;
            this.splitContainer1.TabIndex = 7;
            // 
            // clientPermissionsGroupBox
            // 
            this.clientPermissionsGroupBox.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
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
            this.clientPermissionsGroupBox.Location = new System.Drawing.Point(813, 62);
            this.clientPermissionsGroupBox.Name = "clientPermissionsGroupBox";
            this.clientPermissionsGroupBox.Size = new System.Drawing.Size(177, 328);
            this.clientPermissionsGroupBox.TabIndex = 28;
            this.clientPermissionsGroupBox.TabStop = false;
            this.clientPermissionsGroupBox.Text = "Permissions";
            // 
            // startPlaybackCheckbox
            // 
            this.startPlaybackCheckbox.AutoSize = true;
            this.startPlaybackCheckbox.Location = new System.Drawing.Point(6, 25);
            this.startPlaybackCheckbox.Name = "startPlaybackCheckbox";
            this.startPlaybackCheckbox.Size = new System.Drawing.Size(137, 24);
            this.startPlaybackCheckbox.TabIndex = 18;
            this.startPlaybackCheckbox.Text = "Start Playback";
            this.startPlaybackCheckbox.UseVisualStyleBackColor = true;
            this.startPlaybackCheckbox.CheckedChanged += new System.EventHandler(this.StartPlaybackCheckbox_CheckedChanged);
            // 
            // changeMuteCheckBox
            // 
            this.changeMuteCheckBox.AutoSize = true;
            this.changeMuteCheckBox.Location = new System.Drawing.Point(6, 295);
            this.changeMuteCheckBox.Name = "changeMuteCheckBox";
            this.changeMuteCheckBox.Size = new System.Drawing.Size(71, 24);
            this.changeMuteCheckBox.TabIndex = 27;
            this.changeMuteCheckBox.Text = "Mute";
            this.changeMuteCheckBox.UseVisualStyleBackColor = true;
            this.changeMuteCheckBox.CheckedChanged += new System.EventHandler(this.ChangeMuteCheckBox_CheckedChanged);
            // 
            // stopPlaybackCheckbox
            // 
            this.stopPlaybackCheckbox.AutoSize = true;
            this.stopPlaybackCheckbox.Location = new System.Drawing.Point(6, 55);
            this.stopPlaybackCheckbox.Name = "stopPlaybackCheckbox";
            this.stopPlaybackCheckbox.Size = new System.Drawing.Size(136, 24);
            this.stopPlaybackCheckbox.TabIndex = 19;
            this.stopPlaybackCheckbox.Text = "Stop Playback";
            this.stopPlaybackCheckbox.UseVisualStyleBackColor = true;
            this.stopPlaybackCheckbox.CheckedChanged += new System.EventHandler(this.StopPlaybackCheckbox_CheckedChanged);
            // 
            // changeRepeatCheckBox
            // 
            this.changeRepeatCheckBox.AutoSize = true;
            this.changeRepeatCheckBox.Location = new System.Drawing.Point(6, 265);
            this.changeRepeatCheckBox.Name = "changeRepeatCheckBox";
            this.changeRepeatCheckBox.Size = new System.Drawing.Size(148, 24);
            this.changeRepeatCheckBox.TabIndex = 26;
            this.changeRepeatCheckBox.Text = "Change Repeat";
            this.changeRepeatCheckBox.UseVisualStyleBackColor = true;
            this.changeRepeatCheckBox.CheckedChanged += new System.EventHandler(this.ChangeRepeatCheckBox_CheckedChanged);
            // 
            // playNextCheckbox
            // 
            this.playNextCheckbox.AutoSize = true;
            this.playNextCheckbox.Location = new System.Drawing.Point(6, 85);
            this.playNextCheckbox.Name = "playNextCheckbox";
            this.playNextCheckbox.Size = new System.Drawing.Size(100, 24);
            this.playNextCheckbox.TabIndex = 20;
            this.playNextCheckbox.Text = "Play Next";
            this.playNextCheckbox.UseVisualStyleBackColor = true;
            this.playNextCheckbox.CheckedChanged += new System.EventHandler(this.PlayNextCheckbox_CheckedChanged);
            // 
            // changeShuffleCheckBox
            // 
            this.changeShuffleCheckBox.AutoSize = true;
            this.changeShuffleCheckBox.Location = new System.Drawing.Point(6, 235);
            this.changeShuffleCheckBox.Name = "changeShuffleCheckBox";
            this.changeShuffleCheckBox.Size = new System.Drawing.Size(146, 24);
            this.changeShuffleCheckBox.TabIndex = 25;
            this.changeShuffleCheckBox.Text = "Change Shuffle";
            this.changeShuffleCheckBox.UseVisualStyleBackColor = true;
            this.changeShuffleCheckBox.CheckedChanged += new System.EventHandler(this.ChangeShuffleCheckBox_CheckedChanged);
            // 
            // playPreviousCheckBox
            // 
            this.playPreviousCheckBox.AutoSize = true;
            this.playPreviousCheckBox.Location = new System.Drawing.Point(6, 115);
            this.playPreviousCheckBox.Name = "playPreviousCheckBox";
            this.playPreviousCheckBox.Size = new System.Drawing.Size(128, 24);
            this.playPreviousCheckBox.TabIndex = 21;
            this.playPreviousCheckBox.Text = "Play Previous";
            this.playPreviousCheckBox.UseVisualStyleBackColor = true;
            this.playPreviousCheckBox.CheckedChanged += new System.EventHandler(this.PlayPreviousCheckBox_CheckedChanged);
            // 
            // changeVolumeCheckbox
            // 
            this.changeVolumeCheckbox.AutoSize = true;
            this.changeVolumeCheckbox.Location = new System.Drawing.Point(6, 205);
            this.changeVolumeCheckbox.Name = "changeVolumeCheckbox";
            this.changeVolumeCheckbox.Size = new System.Drawing.Size(149, 24);
            this.changeVolumeCheckbox.TabIndex = 24;
            this.changeVolumeCheckbox.Text = "Change Volume";
            this.changeVolumeCheckbox.UseVisualStyleBackColor = true;
            this.changeVolumeCheckbox.CheckedChanged += new System.EventHandler(this.ChangeVolumeCheckbox_CheckedChanged);
            // 
            // addTrackCheckbox
            // 
            this.addTrackCheckbox.AutoSize = true;
            this.addTrackCheckbox.Location = new System.Drawing.Point(6, 145);
            this.addTrackCheckbox.Name = "addTrackCheckbox";
            this.addTrackCheckbox.Size = new System.Drawing.Size(107, 24);
            this.addTrackCheckbox.TabIndex = 22;
            this.addTrackCheckbox.Text = "Add Track";
            this.addTrackCheckbox.UseVisualStyleBackColor = true;
            this.addTrackCheckbox.CheckedChanged += new System.EventHandler(this.AddTrackCheckbox_CheckedChanged);
            // 
            // removeTrackCheckbox
            // 
            this.removeTrackCheckbox.AutoSize = true;
            this.removeTrackCheckbox.Location = new System.Drawing.Point(6, 175);
            this.removeTrackCheckbox.Name = "removeTrackCheckbox";
            this.removeTrackCheckbox.Size = new System.Drawing.Size(133, 24);
            this.removeTrackCheckbox.TabIndex = 23;
            this.removeTrackCheckbox.Text = "RemoveTrack";
            this.removeTrackCheckbox.UseVisualStyleBackColor = true;
            this.removeTrackCheckbox.CheckedChanged += new System.EventHandler(this.RemoveTrackCheckbox_CheckedChanged);
            // 
            // logGrid
            // 
            this.logGrid.AllowUserToAddRows = false;
            this.logGrid.AllowUserToDeleteRows = false;
            this.logGrid.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.logGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.AllCells;
            this.logGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.logGrid.Location = new System.Drawing.Point(7, 23);
            this.logGrid.Name = "logGrid";
            this.logGrid.ReadOnly = true;
            this.logGrid.RowTemplate.Height = 28;
            this.logGrid.Size = new System.Drawing.Size(983, 280);
            this.logGrid.TabIndex = 4;
            // 
            // numericUpDown1
            // 
            this.numericUpDown1.Location = new System.Drawing.Point(909, 10);
            this.numericUpDown1.Name = "numericUpDown1";
            this.numericUpDown1.Size = new System.Drawing.Size(81, 26);
            this.numericUpDown1.TabIndex = 29;
            // 
            // PartyModePanel
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1002, 712);
            this.Controls.Add(this.splitContainer1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Name = "PartyModePanel";
            this.Text = "MusicBeeRemote: Party Mode Permissions";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.PartyModePanel_FormClosing);
            this.Load += new System.EventHandler(this.PartyModePanel_Load);
            ((System.ComponentModel.ISupportInitialize)(this.clientListGrid)).EndInit();
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel1.PerformLayout();
            this.splitContainer1.Panel2.ResumeLayout(false);
            this.splitContainer1.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.clientPermissionsGroupBox.ResumeLayout(false);
            this.clientPermissionsGroupBox.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.logGrid)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown1)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.DataGridView clientListGrid;
        private System.Windows.Forms.Label knownClientsLabel;
        private System.Windows.Forms.Label logsLabel;
        private System.Windows.Forms.CheckBox activeCheckbox;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.DataGridView logGrid;
        private System.Windows.Forms.GroupBox clientPermissionsGroupBox;
        private System.Windows.Forms.CheckBox startPlaybackCheckbox;
        private System.Windows.Forms.CheckBox changeMuteCheckBox;
        private System.Windows.Forms.CheckBox stopPlaybackCheckbox;
        private System.Windows.Forms.CheckBox changeRepeatCheckBox;
        private System.Windows.Forms.CheckBox playNextCheckbox;
        private System.Windows.Forms.CheckBox changeShuffleCheckBox;
        private System.Windows.Forms.CheckBox playPreviousCheckBox;
        private System.Windows.Forms.CheckBox changeVolumeCheckbox;
        private System.Windows.Forms.CheckBox addTrackCheckbox;
        private System.Windows.Forms.CheckBox removeTrackCheckbox;
        private System.Windows.Forms.NumericUpDown numericUpDown1;
    }
}