namespace MusicBeeRemote.Core.Settings.Dialog.BasePanel
{
    partial class ConfigurationPanel
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
            this.components = new System.ComponentModel.Container();
            this.statusLabel = new System.Windows.Forms.Label();
            this.connectionSettingsLabel = new System.Windows.Forms.Label();
            this.listeningPortLabel = new System.Windows.Forms.Label();
            this.listeningPortNumber = new System.Windows.Forms.TextBox();
            this.statusValueLabel = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.filteringOptionsComboBox = new System.Windows.Forms.ComboBox();
            this.allowedLabel = new System.Windows.Forms.Label();
            this.privateIpAddressLabel = new System.Windows.Forms.Label();
            this.openHelpButton = new System.Windows.Forms.Button();
            this.saveButton = new System.Windows.Forms.Button();
            this.pluginVersionLabel = new System.Windows.Forms.Label();
            this.versionValueLabel = new System.Windows.Forms.Label();
            this.miscLabel = new System.Windows.Forms.Label();
            this.updateFirewallSettingsCheckbox = new System.Windows.Forms.CheckBox();
            this.label2 = new System.Windows.Forms.Label();
            this.enableDebugLoggingCheckbox = new System.Windows.Forms.CheckBox();
            this.openLogDirectoryButton = new System.Windows.Forms.Button();
            this.filteringPanel = new System.Windows.Forms.Panel();
            this.clientAddressList = new System.Windows.Forms.ListBox();
            this.listeningPortErrorProvider = new System.Windows.Forms.ErrorProvider(this.components);
            ((System.ComponentModel.ISupportInitialize)(this.listeningPortErrorProvider)).BeginInit();
            this.SuspendLayout();
            // 
            // statusLabel
            // 
            this.statusLabel.AutoSize = true;
            this.statusLabel.Location = new System.Drawing.Point(12, 31);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(60, 20);
            this.statusLabel.TabIndex = 0;
            this.statusLabel.Text = "Status:";
            // 
            // connectionSettingsLabel
            // 
            this.connectionSettingsLabel.AutoSize = true;
            this.connectionSettingsLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.connectionSettingsLabel.Location = new System.Drawing.Point(12, 85);
            this.connectionSettingsLabel.Name = "connectionSettingsLabel";
            this.connectionSettingsLabel.Size = new System.Drawing.Size(172, 20);
            this.connectionSettingsLabel.TabIndex = 1;
            this.connectionSettingsLabel.Text = "Connection Settings";
            // 
            // listeningPortLabel
            // 
            this.listeningPortLabel.AutoSize = true;
            this.listeningPortLabel.Location = new System.Drawing.Point(12, 127);
            this.listeningPortLabel.Name = "listeningPortLabel";
            this.listeningPortLabel.Size = new System.Drawing.Size(110, 20);
            this.listeningPortLabel.TabIndex = 2;
            this.listeningPortLabel.Text = "Listening Port:";
            // 
            // listeningPortNumber
            // 
            this.listeningPortNumber.Location = new System.Drawing.Point(207, 124);
            this.listeningPortNumber.MaxLength = 5;
            this.listeningPortNumber.Name = "listeningPortNumber";
            this.listeningPortNumber.Size = new System.Drawing.Size(121, 26);
            this.listeningPortNumber.TabIndex = 3;
            this.listeningPortNumber.Text = "30000";
            this.listeningPortNumber.TextChanged += new System.EventHandler(this.listeningPortNumber_TextChanged);
            // 
            // statusValueLabel
            // 
            this.statusValueLabel.AutoSize = true;
            this.statusValueLabel.ForeColor = System.Drawing.Color.Red;
            this.statusValueLabel.Location = new System.Drawing.Point(203, 31);
            this.statusValueLabel.Name = "statusValueLabel";
            this.statusValueLabel.Size = new System.Drawing.Size(70, 20);
            this.statusValueLabel.TabIndex = 4;
            this.statusValueLabel.Text = "Stopped";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.Location = new System.Drawing.Point(12, 175);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(125, 20);
            this.label1.TabIndex = 5;
            this.label1.Text = "Client Filtering";
            // 
            // filteringOptionsComboBox
            // 
            this.filteringOptionsComboBox.FormattingEnabled = true;
            this.filteringOptionsComboBox.Location = new System.Drawing.Point(207, 214);
            this.filteringOptionsComboBox.Name = "filteringOptionsComboBox";
            this.filteringOptionsComboBox.Size = new System.Drawing.Size(121, 28);
            this.filteringOptionsComboBox.TabIndex = 6;
            this.filteringOptionsComboBox.SelectedIndexChanged += new System.EventHandler(this.FilteringOptionsComboBox_SelectedIndexChanged);
            // 
            // allowedLabel
            // 
            this.allowedLabel.AutoSize = true;
            this.allowedLabel.Location = new System.Drawing.Point(16, 214);
            this.allowedLabel.Name = "allowedLabel";
            this.allowedLabel.Size = new System.Drawing.Size(68, 20);
            this.allowedLabel.TabIndex = 7;
            this.allowedLabel.Text = "Allowed:";
            // 
            // privateIpAddressLabel
            // 
            this.privateIpAddressLabel.AutoSize = true;
            this.privateIpAddressLabel.Location = new System.Drawing.Point(629, 62);
            this.privateIpAddressLabel.Name = "privateIpAddressLabel";
            this.privateIpAddressLabel.Size = new System.Drawing.Size(149, 20);
            this.privateIpAddressLabel.TabIndex = 9;
            this.privateIpAddressLabel.Text = "Private Address List";
            // 
            // openHelpButton
            // 
            this.openHelpButton.Location = new System.Drawing.Point(684, 618);
            this.openHelpButton.Name = "openHelpButton";
            this.openHelpButton.Size = new System.Drawing.Size(94, 41);
            this.openHelpButton.TabIndex = 10;
            this.openHelpButton.Text = "Help";
            this.openHelpButton.UseVisualStyleBackColor = true;
            this.openHelpButton.Click += new System.EventHandler(this.OpenHelpButtonClick);
            // 
            // saveButton
            // 
            this.saveButton.Location = new System.Drawing.Point(584, 618);
            this.saveButton.Name = "saveButton";
            this.saveButton.Size = new System.Drawing.Size(94, 41);
            this.saveButton.TabIndex = 11;
            this.saveButton.Text = "Save";
            this.saveButton.UseVisualStyleBackColor = true;
            this.saveButton.Click += new System.EventHandler(this.SaveButtonClick);
            // 
            // pluginVersionLabel
            // 
            this.pluginVersionLabel.AutoSize = true;
            this.pluginVersionLabel.Location = new System.Drawing.Point(12, 642);
            this.pluginVersionLabel.Name = "pluginVersionLabel";
            this.pluginVersionLabel.Size = new System.Drawing.Size(67, 20);
            this.pluginVersionLabel.TabIndex = 12;
            this.pluginVersionLabel.Text = "Version:";
            // 
            // versionValueLabel
            // 
            this.versionValueLabel.AutoSize = true;
            this.versionValueLabel.Location = new System.Drawing.Point(85, 642);
            this.versionValueLabel.Name = "versionValueLabel";
            this.versionValueLabel.Size = new System.Drawing.Size(57, 20);
            this.versionValueLabel.TabIndex = 13;
            this.versionValueLabel.Text = "1.0.0.0";
            // 
            // miscLabel
            // 
            this.miscLabel.AutoSize = true;
            this.miscLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.miscLabel.Location = new System.Drawing.Point(12, 407);
            this.miscLabel.Name = "miscLabel";
            this.miscLabel.Size = new System.Drawing.Size(122, 20);
            this.miscLabel.TabIndex = 14;
            this.miscLabel.Text = "Miscellaneous";
            // 
            // updateFirewallSettingsCheckbox
            // 
            this.updateFirewallSettingsCheckbox.AutoSize = true;
            this.updateFirewallSettingsCheckbox.Location = new System.Drawing.Point(16, 446);
            this.updateFirewallSettingsCheckbox.Name = "updateFirewallSettingsCheckbox";
            this.updateFirewallSettingsCheckbox.Size = new System.Drawing.Size(208, 24);
            this.updateFirewallSettingsCheckbox.TabIndex = 15;
            this.updateFirewallSettingsCheckbox.Text = "Update Firewall Settings";
            this.updateFirewallSettingsCheckbox.UseVisualStyleBackColor = true;
            this.updateFirewallSettingsCheckbox.CheckedChanged += new System.EventHandler(this.UpdateFirewallSettingsCheckboxCheckedChanged);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(12, 494);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(62, 20);
            this.label2.TabIndex = 16;
            this.label2.Text = "Debug";
            // 
            // enableDebugLoggingCheckbox
            // 
            this.enableDebugLoggingCheckbox.AutoSize = true;
            this.enableDebugLoggingCheckbox.Location = new System.Drawing.Point(16, 531);
            this.enableDebugLoggingCheckbox.Name = "enableDebugLoggingCheckbox";
            this.enableDebugLoggingCheckbox.Size = new System.Drawing.Size(198, 24);
            this.enableDebugLoggingCheckbox.TabIndex = 17;
            this.enableDebugLoggingCheckbox.Text = "Enable Debug Logging";
            this.enableDebugLoggingCheckbox.UseVisualStyleBackColor = true;
            this.enableDebugLoggingCheckbox.CheckedChanged += new System.EventHandler(this.EnableDebugLoggingCheckboxCheckedChanged);
            // 
            // openLogDirectoryButton
            // 
            this.openLogDirectoryButton.Location = new System.Drawing.Point(16, 561);
            this.openLogDirectoryButton.Name = "openLogDirectoryButton";
            this.openLogDirectoryButton.Size = new System.Drawing.Size(198, 41);
            this.openLogDirectoryButton.TabIndex = 18;
            this.openLogDirectoryButton.Text = "Open Log Directory";
            this.openLogDirectoryButton.UseVisualStyleBackColor = true;
            this.openLogDirectoryButton.Click += new System.EventHandler(this.OpenLogDirectoryButtonClick);
            // 
            // filteringPanel
            // 
            this.filteringPanel.Location = new System.Drawing.Point(20, 262);
            this.filteringPanel.Name = "filteringPanel";
            this.filteringPanel.Size = new System.Drawing.Size(308, 137);
            this.filteringPanel.TabIndex = 19;
            // 
            // clientAddressList
            // 
            this.clientAddressList.FormattingEnabled = true;
            this.clientAddressList.ItemHeight = 20;
            this.clientAddressList.Location = new System.Drawing.Point(584, 85);
            this.clientAddressList.Name = "clientAddressList";
            this.clientAddressList.Size = new System.Drawing.Size(194, 284);
            this.clientAddressList.TabIndex = 20;
            // 
            // listeningPortErrorProvider
            // 
            this.listeningPortErrorProvider.ContainerControl = this;
            // 
            // ConfigurationPanel
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(790, 671);
            this.Controls.Add(this.clientAddressList);
            this.Controls.Add(this.filteringPanel);
            this.Controls.Add(this.openLogDirectoryButton);
            this.Controls.Add(this.enableDebugLoggingCheckbox);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.updateFirewallSettingsCheckbox);
            this.Controls.Add(this.miscLabel);
            this.Controls.Add(this.versionValueLabel);
            this.Controls.Add(this.pluginVersionLabel);
            this.Controls.Add(this.saveButton);
            this.Controls.Add(this.openHelpButton);
            this.Controls.Add(this.privateIpAddressLabel);
            this.Controls.Add(this.allowedLabel);
            this.Controls.Add(this.filteringOptionsComboBox);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.statusValueLabel);
            this.Controls.Add(this.listeningPortNumber);
            this.Controls.Add(this.listeningPortLabel);
            this.Controls.Add(this.connectionSettingsLabel);
            this.Controls.Add(this.statusLabel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Name = "ConfigurationPanel";
            this.Text = "MusicBee Remote";
            ((System.ComponentModel.ISupportInitialize)(this.listeningPortErrorProvider)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label statusLabel;
        private System.Windows.Forms.Label connectionSettingsLabel;
        private System.Windows.Forms.Label listeningPortLabel;
        private System.Windows.Forms.TextBox listeningPortNumber;
        private System.Windows.Forms.Label statusValueLabel;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.ComboBox filteringOptionsComboBox;
        private System.Windows.Forms.Label allowedLabel;
        private System.Windows.Forms.Label privateIpAddressLabel;
        private System.Windows.Forms.Button openHelpButton;
        private System.Windows.Forms.Button saveButton;
        private System.Windows.Forms.Label pluginVersionLabel;
        private System.Windows.Forms.Label versionValueLabel;
        private System.Windows.Forms.Label miscLabel;
        private System.Windows.Forms.CheckBox updateFirewallSettingsCheckbox;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.CheckBox enableDebugLoggingCheckbox;
        private System.Windows.Forms.Button openLogDirectoryButton;
        private System.Windows.Forms.Panel filteringPanel;
        private System.Windows.Forms.ListBox clientAddressList;
        private System.Windows.Forms.ErrorProvider listeningPortErrorProvider;
    }
}