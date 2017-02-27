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
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.firewallCheckbox = new System.Windows.Forms.CheckBox();
            this.openLogButton = new System.Windows.Forms.Button();
            this.debugEnabled = new System.Windows.Forms.CheckBox();
            this.saveButton = new System.Windows.Forms.Button();
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
            this.helpButton = new System.Windows.Forms.Button();
            this.label3 = new System.Windows.Forms.Label();
            this.statusLabel = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.internalIPList = new System.Windows.Forms.ListBox();
            this.versionLabel = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.partyModeTP = new System.Windows.Forms.TabPage();
            this.elementHost1 = new System.Windows.Forms.Integration.ElementHost();
            this.tabControl1.SuspendLayout();
            this.tabPage1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.rangeNumericUpDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.portNumericUpDown)).BeginInit();
            this.partyModeTP.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Controls.Add(this.partyModeTP);
            this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl1.Location = new System.Drawing.Point(0, 0);
            this.tabControl1.Margin = new System.Windows.Forms.Padding(4);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(599, 473);
            this.tabControl1.TabIndex = 39;
            // 
            // tabPage1
            // 
            this.tabPage1.Controls.Add(this.firewallCheckbox);
            this.tabPage1.Controls.Add(this.openLogButton);
            this.tabPage1.Controls.Add(this.debugEnabled);
            this.tabPage1.Controls.Add(this.saveButton);
            this.tabPage1.Controls.Add(this.rangeNumericUpDown);
            this.tabPage1.Controls.Add(this.addAddressButton);
            this.tabPage1.Controls.Add(this.allowedLabel);
            this.tabPage1.Controls.Add(this.allowedAddressesComboBox);
            this.tabPage1.Controls.Add(this.removeAddressButton);
            this.tabPage1.Controls.Add(this.addressLabel);
            this.tabPage1.Controls.Add(this.ipAddressInputTextBox);
            this.tabPage1.Controls.Add(this.allowLabel);
            this.tabPage1.Controls.Add(this.selectionFilteringComboBox);
            this.tabPage1.Controls.Add(this.seperator2);
            this.tabPage1.Controls.Add(this.addressFilteringCategoryLabel);
            this.tabPage1.Controls.Add(this.portNumericUpDown);
            this.tabPage1.Controls.Add(this.portLabel);
            this.tabPage1.Controls.Add(this.seperator1);
            this.tabPage1.Controls.Add(this.connectionSettingsCategoryLabel);
            this.tabPage1.Controls.Add(this.helpButton);
            this.tabPage1.Controls.Add(this.label3);
            this.tabPage1.Controls.Add(this.statusLabel);
            this.tabPage1.Controls.Add(this.label2);
            this.tabPage1.Controls.Add(this.internalIPList);
            this.tabPage1.Controls.Add(this.versionLabel);
            this.tabPage1.Controls.Add(this.label1);
            this.tabPage1.Location = new System.Drawing.Point(4, 25);
            this.tabPage1.Margin = new System.Windows.Forms.Padding(4);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(4);
            this.tabPage1.Size = new System.Drawing.Size(591, 444);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "main ";
            this.tabPage1.UseVisualStyleBackColor = true;
            // 
            // firewallCheckbox
            // 
            this.firewallCheckbox.AutoSize = true;
            this.firewallCheckbox.Location = new System.Drawing.Point(302, 365);
            this.firewallCheckbox.Margin = new System.Windows.Forms.Padding(4);
            this.firewallCheckbox.Name = "firewallCheckbox";
            this.firewallCheckbox.Size = new System.Drawing.Size(127, 21);
            this.firewallCheckbox.TabIndex = 64;
            this.firewallCheckbox.Text = "Update Firewall";
            this.firewallCheckbox.UseVisualStyleBackColor = true;
            // 
            // openLogButton
            // 
            this.openLogButton.Location = new System.Drawing.Point(13, 340);
            this.openLogButton.Margin = new System.Windows.Forms.Padding(4);
            this.openLogButton.Name = "openLogButton";
            this.openLogButton.Size = new System.Drawing.Size(100, 28);
            this.openLogButton.TabIndex = 63;
            this.openLogButton.Text = "Open Log";
            this.openLogButton.UseVisualStyleBackColor = true;
            this.openLogButton.Click += new System.EventHandler(this.OpenLogButtonClick);
            // 
            // debugEnabled
            // 
            this.debugEnabled.AutoSize = true;
            this.debugEnabled.Location = new System.Drawing.Point(13, 282);
            this.debugEnabled.Margin = new System.Windows.Forms.Padding(4);
            this.debugEnabled.Name = "debugEnabled";
            this.debugEnabled.Size = new System.Drawing.Size(100, 21);
            this.debugEnabled.TabIndex = 62;
            this.debugEnabled.Text = "Debug Log";
            this.debugEnabled.UseVisualStyleBackColor = true;
            this.debugEnabled.CheckedChanged += new System.EventHandler(this.DebugCheckboxCheckedChanged);
            // 
            // saveButton
            // 
            this.saveButton.Location = new System.Drawing.Point(302, 394);
            this.saveButton.Margin = new System.Windows.Forms.Padding(4);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new System.Drawing.Size(100, 28);
            this.saveButton.TabIndex = 61;
            this.saveButton.Text = "Save";
            this.saveButton.UseVisualStyleBackColor = true;
            this.saveButton.Click += new System.EventHandler(this.HandleSaveButtonClick);
            // 
            // rangeNumericUpDown
            // 
            this.rangeNumericUpDown.Location = new System.Drawing.Point(302, 195);
            this.rangeNumericUpDown.Margin = new System.Windows.Forms.Padding(4);
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
            this.rangeNumericUpDown.Size = new System.Drawing.Size(57, 22);
            this.rangeNumericUpDown.TabIndex = 59;
            this.rangeNumericUpDown.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // addAddressButton
            // 
            this.addAddressButton.Location = new System.Drawing.Point(302, 233);
            this.addAddressButton.Margin = new System.Windows.Forms.Padding(4);
            this.addAddressButton.Name = "addAddressButton";
            this.addAddressButton.Size = new System.Drawing.Size(28, 26);
            this.addAddressButton.TabIndex = 46;
            this.addAddressButton.Text = "+";
            this.addAddressButton.UseVisualStyleBackColor = true;
            // 
            // allowedLabel
            // 
            this.allowedLabel.AutoSize = true;
            this.allowedLabel.Location = new System.Drawing.Point(9, 237);
            this.allowedLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.allowedLabel.Name = "allowedLabel";
            this.allowedLabel.Size = new System.Drawing.Size(60, 17);
            this.allowedLabel.TabIndex = 60;
            this.allowedLabel.Text = "Allowed:";
            // 
            // allowedAddressesComboBox
            // 
            this.allowedAddressesComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.allowedAddressesComboBox.FormattingEnabled = true;
            this.allowedAddressesComboBox.Location = new System.Drawing.Point(148, 233);
            this.allowedAddressesComboBox.Margin = new System.Windows.Forms.Padding(4);
            this.allowedAddressesComboBox.Name = "allowedAddressesComboBox";
            this.allowedAddressesComboBox.Size = new System.Drawing.Size(145, 24);
            this.allowedAddressesComboBox.TabIndex = 50;
            // 
            // removeAddressButton
            // 
            this.removeAddressButton.Location = new System.Drawing.Point(332, 233);
            this.removeAddressButton.Margin = new System.Windows.Forms.Padding(4);
            this.removeAddressButton.Name = "removeAddressButton";
            this.removeAddressButton.Size = new System.Drawing.Size(28, 26);
            this.removeAddressButton.TabIndex = 48;
            this.removeAddressButton.Text = "-";
            this.removeAddressButton.UseVisualStyleBackColor = true;
            this.removeAddressButton.Click += new System.EventHandler(this.RemoveAddressButtonClick);
            // 
            // addressLabel
            // 
            this.addressLabel.AutoSize = true;
            this.addressLabel.Location = new System.Drawing.Point(8, 198);
            this.addressLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.addressLabel.Name = "addressLabel";
            this.addressLabel.Size = new System.Drawing.Size(64, 17);
            this.addressLabel.TabIndex = 58;
            this.addressLabel.Text = "Address:";
            // 
            // ipAddressInputTextBox
            // 
            this.ipAddressInputTextBox.Location = new System.Drawing.Point(148, 195);
            this.ipAddressInputTextBox.Margin = new System.Windows.Forms.Padding(4);
            this.ipAddressInputTextBox.Name = "ipAddressInputTextBox";
            this.ipAddressInputTextBox.Size = new System.Drawing.Size(145, 22);
            this.ipAddressInputTextBox.TabIndex = 57;
            this.ipAddressInputTextBox.TextChanged += new System.EventHandler(this.HandleIpAddressInputTextBoxTextChanged);
            // 
            // allowLabel
            // 
            this.allowLabel.AutoSize = true;
            this.allowLabel.Location = new System.Drawing.Point(8, 161);
            this.allowLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.allowLabel.Name = "allowLabel";
            this.allowLabel.Size = new System.Drawing.Size(44, 17);
            this.allowLabel.TabIndex = 56;
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
            this.selectionFilteringComboBox.Location = new System.Drawing.Point(148, 158);
            this.selectionFilteringComboBox.Margin = new System.Windows.Forms.Padding(4);
            this.selectionFilteringComboBox.Name = "selectionFilteringComboBox";
            this.selectionFilteringComboBox.Size = new System.Drawing.Size(211, 24);
            this.selectionFilteringComboBox.TabIndex = 55;
            this.selectionFilteringComboBox.SelectedIndexChanged += new System.EventHandler(this.SelectionFilteringComboBoxSelectedIndexChanged);
            // 
            // seperator2
            // 
            this.seperator2.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.seperator2.Location = new System.Drawing.Point(6, 141);
            this.seperator2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.seperator2.Name = "seperator2";
            this.seperator2.Size = new System.Drawing.Size(353, 1);
            this.seperator2.TabIndex = 54;
            // 
            // addressFilteringCategoryLabel
            // 
            this.addressFilteringCategoryLabel.AutoSize = true;
            this.addressFilteringCategoryLabel.Location = new System.Drawing.Point(8, 123);
            this.addressFilteringCategoryLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.addressFilteringCategoryLabel.Name = "addressFilteringCategoryLabel";
            this.addressFilteringCategoryLabel.Size = new System.Drawing.Size(114, 17);
            this.addressFilteringCategoryLabel.TabIndex = 53;
            this.addressFilteringCategoryLabel.Text = "Address Filtering";
            // 
            // portNumericUpDown
            // 
            this.portNumericUpDown.Location = new System.Drawing.Point(148, 78);
            this.portNumericUpDown.Margin = new System.Windows.Forms.Padding(4);
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
            this.portNumericUpDown.Size = new System.Drawing.Size(212, 22);
            this.portNumericUpDown.TabIndex = 52;
            this.portNumericUpDown.Value = new decimal(new int[] {
            3000,
            0,
            0,
            0});
            // 
            // portLabel
            // 
            this.portLabel.AutoSize = true;
            this.portLabel.Location = new System.Drawing.Point(8, 80);
            this.portLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.portLabel.Name = "portLabel";
            this.portLabel.Size = new System.Drawing.Size(38, 17);
            this.portLabel.TabIndex = 51;
            this.portLabel.Text = "Port:";
            // 
            // seperator1
            // 
            this.seperator1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.seperator1.Location = new System.Drawing.Point(6, 65);
            this.seperator1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.seperator1.Name = "seperator1";
            this.seperator1.Size = new System.Drawing.Size(353, 1);
            this.seperator1.TabIndex = 49;
            // 
            // connectionSettingsCategoryLabel
            // 
            this.connectionSettingsCategoryLabel.AutoSize = true;
            this.connectionSettingsCategoryLabel.Location = new System.Drawing.Point(8, 48);
            this.connectionSettingsCategoryLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.connectionSettingsCategoryLabel.Name = "connectionSettingsCategoryLabel";
            this.connectionSettingsCategoryLabel.Size = new System.Drawing.Size(134, 17);
            this.connectionSettingsCategoryLabel.TabIndex = 47;
            this.connectionSettingsCategoryLabel.Text = "Connection Settings";
            // 
            // helpButton
            // 
            this.helpButton.Location = new System.Drawing.Point(449, 394);
            this.helpButton.Margin = new System.Windows.Forms.Padding(4);
            this.helpButton.Name = "helpButton";
            this.helpButton.Size = new System.Drawing.Size(123, 28);
            this.helpButton.TabIndex = 45;
            this.helpButton.Text = "Help";
            this.helpButton.UseVisualStyleBackColor = true;
            this.helpButton.Click += new System.EventHandler(this.HelpButtonClick);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(410, 52);
            this.label3.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(128, 17);
            this.label3.TabIndex = 44;
            this.label3.Text = "Private address list";
            // 
            // statusLabel
            // 
            this.statusLabel.AutoSize = true;
            this.statusLabel.ForeColor = System.Drawing.Color.Green;
            this.statusLabel.Location = new System.Drawing.Point(70, 21);
            this.statusLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(48, 17);
            this.statusLabel.TabIndex = 43;
            this.statusLabel.Text = "Status";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(10, 21);
            this.label2.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(52, 17);
            this.label2.TabIndex = 42;
            this.label2.Text = "Status:";
            // 
            // internalIPList
            // 
            this.internalIPList.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.internalIPList.FormattingEnabled = true;
            this.internalIPList.ItemHeight = 16;
            this.internalIPList.Location = new System.Drawing.Point(414, 78);
            this.internalIPList.Margin = new System.Windows.Forms.Padding(4);
            this.internalIPList.Name = "internalIPList";
            this.internalIPList.Size = new System.Drawing.Size(158, 226);
            this.internalIPList.TabIndex = 41;
            // 
            // versionLabel
            // 
            this.versionLabel.AutoSize = true;
            this.versionLabel.Location = new System.Drawing.Point(136, 400);
            this.versionLabel.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.versionLabel.Name = "versionLabel";
            this.versionLabel.Size = new System.Drawing.Size(52, 17);
            this.versionLabel.TabIndex = 40;
            this.versionLabel.Text = "0.0.0.0";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(9, 400);
            this.label1.Margin = new System.Windows.Forms.Padding(4, 0, 4, 0);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(103, 17);
            this.label1.TabIndex = 39;
            this.label1.Text = "Plugin Version:";
            // 
            // partyModeTP
            // 
            this.partyModeTP.Controls.Add(this.elementHost1);
            this.partyModeTP.Location = new System.Drawing.Point(4, 25);
            this.partyModeTP.Margin = new System.Windows.Forms.Padding(4);
            this.partyModeTP.Name = "partyModeTP";
            this.partyModeTP.Padding = new System.Windows.Forms.Padding(4);
            this.partyModeTP.Size = new System.Drawing.Size(591, 444);
            this.partyModeTP.TabIndex = 1;
            this.partyModeTP.Text = "party mode";
            this.partyModeTP.UseVisualStyleBackColor = true;
            // 
            // elementHost1
            // 
            this.elementHost1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.elementHost1.Location = new System.Drawing.Point(4, 4);
            this.elementHost1.Margin = new System.Windows.Forms.Padding(4);
            this.elementHost1.Name = "elementHost1";
            this.elementHost1.Size = new System.Drawing.Size(583, 436);
            this.elementHost1.TabIndex = 0;
            this.elementHost1.Text = "elementHost1";
            this.elementHost1.Child = null;
            // 
            // InfoWindow
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(599, 473);
            this.Controls.Add(this.tabControl1);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "InfoWindow";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "MusicBee Remote: plugin";
            this.Load += new System.EventHandler(this.InfoWindowLoad);
            this.tabControl1.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.tabPage1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.rangeNumericUpDown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.portNumericUpDown)).EndInit();
            this.partyModeTP.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.CheckBox firewallCheckbox;
        private System.Windows.Forms.Button openLogButton;
        private System.Windows.Forms.CheckBox debugEnabled;
        private System.Windows.Forms.Button saveButton;
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
        private System.Windows.Forms.Button helpButton;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.ListBox internalIPList;
        private System.Windows.Forms.Label versionLabel;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.TabPage partyModeTP;
        private System.Windows.Forms.Integration.ElementHost elementHost1;
    }
}