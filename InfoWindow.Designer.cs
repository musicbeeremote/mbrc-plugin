namespace MusicBeePlugin
{
    partial class InfoWindow
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
            if (disposing && (this.components != null))
            {
                this.components.Dispose();
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
            this.label1 = new System.Windows.Forms.Label();
            this.versionLabel = new System.Windows.Forms.Label();
            this.checkForUpdateButton = new System.Windows.Forms.Button();
            this.internalIPList = new System.Windows.Forms.ListBox();
            this.label2 = new System.Windows.Forms.Label();
            this.statusLabel = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.helpButton = new System.Windows.Forms.Button();
            this.rangeNumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.addAddressButton = new System.Windows.Forms.Button();
            this.allowedLabel = new System.Windows.Forms.Label();
            this.allowedAddressesComboBox = new System.Windows.Forms.ComboBox();
            this.removeAddressButton = new System.Windows.Forms.Button();
            this.addressLabel = new System.Windows.Forms.Label();
            this.ipAddressInputTextBox = new System.Windows.Forms.TextBox();
            this.allowLabel = new System.Windows.Forms.Label();
            this.selectionFilteringComboBox = new System.Windows.Forms.ComboBox();
            this.seperator2 = new System.Windows.Forms.Label();
            this.addressFilteringCategoryLabel = new System.Windows.Forms.Label();
            this.portNumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.portLabel = new System.Windows.Forms.Label();
            this.seperator1 = new System.Windows.Forms.Label();
            this.connectionSettingsCategoryLabel = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.nowPlayingListLimit = new System.Windows.Forms.NumericUpDown();
            this.saveButton = new System.Windows.Forms.Button();
            this.latestVersionLabel = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.rangeNumericUpDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.portNumericUpDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.nowPlayingListLimit)).BeginInit();
            this.SuspendLayout();
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(3, 393);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(45, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Version:";
            // 
            // versionLabel
            // 
            this.versionLabel.AutoSize = true;
            this.versionLabel.Location = new System.Drawing.Point(59, 393);
            this.versionLabel.Name = "versionLabel";
            this.versionLabel.Size = new System.Drawing.Size(40, 13);
            this.versionLabel.TabIndex = 1;
            this.versionLabel.Text = "0.0.0.0";
            // 
            // checkForUpdateButton
            // 
            this.checkForUpdateButton.Location = new System.Drawing.Point(320, 41);
            this.checkForUpdateButton.Name = "checkForUpdateButton";
            this.checkForUpdateButton.Size = new System.Drawing.Size(119, 23);
            this.checkForUpdateButton.TabIndex = 2;
            this.checkForUpdateButton.Text = "Check for update";
            this.checkForUpdateButton.UseVisualStyleBackColor = true;
            this.checkForUpdateButton.Click += new System.EventHandler(this.HandleCheckForUpdateButtonClick);
            // 
            // internalIPList
            // 
            this.internalIPList.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.internalIPList.FormattingEnabled = true;
            this.internalIPList.Location = new System.Drawing.Point(324, 133);
            this.internalIPList.Name = "internalIPList";
            this.internalIPList.Size = new System.Drawing.Size(119, 145);
            this.internalIPList.TabIndex = 5;
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(13, 13);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(40, 13);
            this.label2.TabIndex = 6;
            this.label2.Text = "Status:";
            // 
            // statusLabel
            // 
            this.statusLabel.AutoSize = true;
            this.statusLabel.ForeColor = System.Drawing.Color.Green;
            this.statusLabel.Location = new System.Drawing.Point(59, 13);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(47, 13);
            this.statusLabel.TabIndex = 7;
            this.statusLabel.Text = "Running";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(321, 112);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(95, 13);
            this.label3.TabIndex = 8;
            this.label3.Text = "Private address list";
            // 
            // helpButton
            // 
            this.helpButton.Location = new System.Drawing.Point(320, 12);
            this.helpButton.Name = "helpButton";
            this.helpButton.Size = new System.Drawing.Size(119, 23);
            this.helpButton.TabIndex = 9;
            this.helpButton.Text = "Help";
            this.helpButton.UseVisualStyleBackColor = true;
            this.helpButton.Click += new System.EventHandler(this.HelpButtonClick);
            // 
            // rangeNumericUpDown
            // 
            this.rangeNumericUpDown.Location = new System.Drawing.Point(234, 208);
            this.rangeNumericUpDown.Maximum = new decimal(new int[] {
            254,
            0,
            0,
            0});
            this.rangeNumericUpDown.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.rangeNumericUpDown.Name = "rangeNumericUpDown";
            this.rangeNumericUpDown.Size = new System.Drawing.Size(43, 20);
            this.rangeNumericUpDown.TabIndex = 28;
            this.rangeNumericUpDown.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // addAddressButton
            // 
            this.addAddressButton.Location = new System.Drawing.Point(234, 233);
            this.addAddressButton.Name = "addAddressButton";
            this.addAddressButton.Size = new System.Drawing.Size(21, 21);
            this.addAddressButton.TabIndex = 15;
            this.addAddressButton.Text = "+";
            this.addAddressButton.UseVisualStyleBackColor = true;
            this.addAddressButton.Click += new System.EventHandler(this.AddAddressButtonClick);
            // 
            // allowedLabel
            // 
            this.allowedLabel.AutoSize = true;
            this.allowedLabel.Location = new System.Drawing.Point(19, 236);
            this.allowedLabel.Name = "allowedLabel";
            this.allowedLabel.Size = new System.Drawing.Size(47, 13);
            this.allowedLabel.TabIndex = 30;
            this.allowedLabel.Text = "Allowed:";
            // 
            // allowedAddressesComboBox
            // 
            this.allowedAddressesComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.allowedAddressesComboBox.FormattingEnabled = true;
            this.allowedAddressesComboBox.Location = new System.Drawing.Point(118, 233);
            this.allowedAddressesComboBox.Name = "allowedAddressesComboBox";
            this.allowedAddressesComboBox.Size = new System.Drawing.Size(110, 21);
            this.allowedAddressesComboBox.TabIndex = 19;
            // 
            // removeAddressButton
            // 
            this.removeAddressButton.Location = new System.Drawing.Point(256, 233);
            this.removeAddressButton.Name = "removeAddressButton";
            this.removeAddressButton.Size = new System.Drawing.Size(21, 21);
            this.removeAddressButton.TabIndex = 17;
            this.removeAddressButton.Text = "-";
            this.removeAddressButton.UseVisualStyleBackColor = true;
            this.removeAddressButton.Click += new System.EventHandler(this.RemoveAddressButtonClick);
            // 
            // addressLabel
            // 
            this.addressLabel.AutoSize = true;
            this.addressLabel.Location = new System.Drawing.Point(19, 210);
            this.addressLabel.Name = "addressLabel";
            this.addressLabel.Size = new System.Drawing.Size(48, 13);
            this.addressLabel.TabIndex = 27;
            this.addressLabel.Text = "Address:";
            // 
            // ipAddressInputTextBox
            // 
            this.ipAddressInputTextBox.Location = new System.Drawing.Point(118, 208);
            this.ipAddressInputTextBox.Name = "ipAddressInputTextBox";
            this.ipAddressInputTextBox.Size = new System.Drawing.Size(110, 20);
            this.ipAddressInputTextBox.TabIndex = 26;
            this.ipAddressInputTextBox.TextChanged += new System.EventHandler(this.HandleIpAddressInputTextBoxTextChanged);
            // 
            // allowLabel
            // 
            this.allowLabel.AutoSize = true;
            this.allowLabel.Location = new System.Drawing.Point(19, 183);
            this.allowLabel.Name = "allowLabel";
            this.allowLabel.Size = new System.Drawing.Size(35, 13);
            this.allowLabel.TabIndex = 25;
            this.allowLabel.Text = "Allow:";
            // 
            // selectionFilteringComboBox
            // 
            this.selectionFilteringComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.selectionFilteringComboBox.FormattingEnabled = true;
            this.selectionFilteringComboBox.Items.AddRange(new object[] {
            "All",
            "Range",
            "Specified"});
            this.selectionFilteringComboBox.Location = new System.Drawing.Point(118, 180);
            this.selectionFilteringComboBox.Name = "selectionFilteringComboBox";
            this.selectionFilteringComboBox.Size = new System.Drawing.Size(159, 21);
            this.selectionFilteringComboBox.TabIndex = 24;
            this.selectionFilteringComboBox.SelectedIndexChanged += new System.EventHandler(this.SelectionFilteringComboBoxSelectedIndexChanged);
            // 
            // seperator2
            // 
            this.seperator2.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.seperator2.Location = new System.Drawing.Point(12, 172);
            this.seperator2.Name = "seperator2";
            this.seperator2.Size = new System.Drawing.Size(265, 1);
            this.seperator2.TabIndex = 23;
            // 
            // addressFilteringCategoryLabel
            // 
            this.addressFilteringCategoryLabel.AutoSize = true;
            this.addressFilteringCategoryLabel.Location = new System.Drawing.Point(13, 158);
            this.addressFilteringCategoryLabel.Name = "addressFilteringCategoryLabel";
            this.addressFilteringCategoryLabel.Size = new System.Drawing.Size(84, 13);
            this.addressFilteringCategoryLabel.TabIndex = 22;
            this.addressFilteringCategoryLabel.Text = "Address Filtering";
            // 
            // portNumericUpDown
            // 
            this.portNumericUpDown.Location = new System.Drawing.Point(118, 130);
            this.portNumericUpDown.Maximum = new decimal(new int[] {
            65535,
            0,
            0,
            0});
            this.portNumericUpDown.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.portNumericUpDown.Name = "portNumericUpDown";
            this.portNumericUpDown.Size = new System.Drawing.Size(159, 20);
            this.portNumericUpDown.TabIndex = 21;
            this.portNumericUpDown.Value = new decimal(new int[] {
            3000,
            0,
            0,
            0});
            // 
            // portLabel
            // 
            this.portLabel.AutoSize = true;
            this.portLabel.Location = new System.Drawing.Point(19, 135);
            this.portLabel.Name = "portLabel";
            this.portLabel.Size = new System.Drawing.Size(29, 13);
            this.portLabel.TabIndex = 20;
            this.portLabel.Text = "Port:";
            // 
            // seperator1
            // 
            this.seperator1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.seperator1.Location = new System.Drawing.Point(12, 126);
            this.seperator1.Name = "seperator1";
            this.seperator1.Size = new System.Drawing.Size(265, 1);
            this.seperator1.TabIndex = 18;
            // 
            // connectionSettingsCategoryLabel
            // 
            this.connectionSettingsCategoryLabel.AutoSize = true;
            this.connectionSettingsCategoryLabel.Location = new System.Drawing.Point(13, 112);
            this.connectionSettingsCategoryLabel.Name = "connectionSettingsCategoryLabel";
            this.connectionSettingsCategoryLabel.Size = new System.Drawing.Size(102, 13);
            this.connectionSettingsCategoryLabel.TabIndex = 16;
            this.connectionSettingsCategoryLabel.Text = "Connection Settings";
            // 
            // label4
            // 
            this.label4.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.label4.Location = new System.Drawing.Point(12, 277);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(265, 1);
            this.label4.TabIndex = 31;
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(13, 262);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(29, 13);
            this.label5.TabIndex = 32;
            this.label5.Text = "Misc";
            // 
            // label6
            // 
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(19, 283);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(93, 13);
            this.label6.TabIndex = 33;
            this.label6.Text = "Now Playing Limit:";
            // 
            // nowPlayingListLimit
            // 
            this.nowPlayingListLimit.Location = new System.Drawing.Point(118, 281);
            this.nowPlayingListLimit.Maximum = new decimal(new int[] {
            100000,
            0,
            0,
            0});
            this.nowPlayingListLimit.Minimum = new decimal(new int[] {
            500,
            0,
            0,
            0});
            this.nowPlayingListLimit.Name = "nowPlayingListLimit";
            this.nowPlayingListLimit.Size = new System.Drawing.Size(159, 20);
            this.nowPlayingListLimit.TabIndex = 34;
            this.nowPlayingListLimit.Value = new decimal(new int[] {
            2000,
            0,
            0,
            0});
            // 
            // saveButton
            // 
            this.saveButton.Location = new System.Drawing.Point(202, 307);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new System.Drawing.Size(75, 23);
            this.saveButton.TabIndex = 35;
            this.saveButton.Text = "Save";
            this.saveButton.UseVisualStyleBackColor = true;
            this.saveButton.Click += new System.EventHandler(this.HandleSaveButtonClick);
            // 
            // latestVersionLabel
            // 
            this.latestVersionLabel.AutoSize = true;
            this.latestVersionLabel.Location = new System.Drawing.Point(106, 393);
            this.latestVersionLabel.Name = "latestVersionLabel";
            this.latestVersionLabel.Size = new System.Drawing.Size(71, 13);
            this.latestVersionLabel.TabIndex = 36;
            this.latestVersionLabel.Text = "LatestVersion";
            // 
            // InfoWindow
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(455, 415);
            this.Controls.Add(this.latestVersionLabel);
            this.Controls.Add(this.saveButton);
            this.Controls.Add(this.nowPlayingListLimit);
            this.Controls.Add(this.label6);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.rangeNumericUpDown);
            this.Controls.Add(this.addAddressButton);
            this.Controls.Add(this.allowedLabel);
            this.Controls.Add(this.allowedAddressesComboBox);
            this.Controls.Add(this.removeAddressButton);
            this.Controls.Add(this.addressLabel);
            this.Controls.Add(this.ipAddressInputTextBox);
            this.Controls.Add(this.allowLabel);
            this.Controls.Add(this.selectionFilteringComboBox);
            this.Controls.Add(this.seperator2);
            this.Controls.Add(this.addressFilteringCategoryLabel);
            this.Controls.Add(this.portNumericUpDown);
            this.Controls.Add(this.portLabel);
            this.Controls.Add(this.seperator1);
            this.Controls.Add(this.connectionSettingsCategoryLabel);
            this.Controls.Add(this.helpButton);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.statusLabel);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.internalIPList);
            this.Controls.Add(this.checkForUpdateButton);
            this.Controls.Add(this.versionLabel);
            this.Controls.Add(this.label1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "InfoWindow";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "MusicBee Remote:server";
            this.Load += new System.EventHandler(this.InfoWindowLoad);
            ((System.ComponentModel.ISupportInitialize)(this.rangeNumericUpDown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.portNumericUpDown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.nowPlayingListLimit)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label versionLabel;
        private System.Windows.Forms.Button checkForUpdateButton;
        private System.Windows.Forms.ListBox internalIPList;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Button helpButton;
        private System.Windows.Forms.NumericUpDown rangeNumericUpDown;
        private System.Windows.Forms.Button addAddressButton;
        private System.Windows.Forms.Label allowedLabel;
        private System.Windows.Forms.ComboBox allowedAddressesComboBox;
        private System.Windows.Forms.Button removeAddressButton;
        private System.Windows.Forms.Label addressLabel;
        private System.Windows.Forms.TextBox ipAddressInputTextBox;
        private System.Windows.Forms.Label allowLabel;
        private System.Windows.Forms.ComboBox selectionFilteringComboBox;
        private System.Windows.Forms.Label seperator2;
        private System.Windows.Forms.Label addressFilteringCategoryLabel;
        private System.Windows.Forms.NumericUpDown portNumericUpDown;
        private System.Windows.Forms.Label portLabel;
        private System.Windows.Forms.Label seperator1;
        private System.Windows.Forms.Label connectionSettingsCategoryLabel;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.NumericUpDown nowPlayingListLimit;
        private System.Windows.Forms.Button saveButton;
        private System.Windows.Forms.Label latestVersionLabel;
    }
}