namespace MusicBeeRemote.Core.Settings.Dialog.Whitelist
{
    partial class WhitelistManagementControl
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.whitelistComboBox = new System.Windows.Forms.ComboBox();
            this.addressRemoveButton = new System.Windows.Forms.Button();
            this.addressAddButton = new System.Windows.Forms.Button();
            this.newAddressTextBox = new System.Windows.Forms.TextBox();
            this.whiteListLabel = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // whitelistComboBox
            // 
            this.whitelistComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.whitelistComboBox.FormattingEnabled = true;
            this.whitelistComboBox.Location = new System.Drawing.Point(9, 40);
            this.whitelistComboBox.Name = "whitelistComboBox";
            this.whitelistComboBox.Size = new System.Drawing.Size(172, 28);
            this.whitelistComboBox.TabIndex = 0;
            // 
            // addressRemoveButton
            // 
            this.addressRemoveButton.Location = new System.Drawing.Point(187, 36);
            this.addressRemoveButton.Name = "addressRemoveButton";
            this.addressRemoveButton.Size = new System.Drawing.Size(95, 34);
            this.addressRemoveButton.TabIndex = 1;
            this.addressRemoveButton.Text = "Remove";
            this.addressRemoveButton.UseVisualStyleBackColor = true;
            this.addressRemoveButton.Click += new System.EventHandler(this.AddressRemoveButtonClick);
            // 
            // addressAddButton
            // 
            this.addressAddButton.Location = new System.Drawing.Point(187, 89);
            this.addressAddButton.Name = "addressAddButton";
            this.addressAddButton.Size = new System.Drawing.Size(95, 34);
            this.addressAddButton.TabIndex = 2;
            this.addressAddButton.Text = "Add";
            this.addressAddButton.UseVisualStyleBackColor = true;
            this.addressAddButton.Click += new System.EventHandler(this.AddressAddButtonClick);
            // 
            // newAddressTextBox
            // 
            this.newAddressTextBox.Location = new System.Drawing.Point(9, 93);
            this.newAddressTextBox.Name = "newAddressTextBox";
            this.newAddressTextBox.Size = new System.Drawing.Size(172, 26);
            this.newAddressTextBox.TabIndex = 3;
            this.newAddressTextBox.TextChanged += new System.EventHandler(this.NewAddressTextBoxTextChanged);
            // 
            // whiteListLabel
            // 
            this.whiteListLabel.AutoSize = true;
            this.whiteListLabel.Location = new System.Drawing.Point(5, 17);
            this.whiteListLabel.Name = "whiteListLabel";
            this.whiteListLabel.Size = new System.Drawing.Size(144, 20);
            this.whiteListLabel.TabIndex = 4;
            this.whiteListLabel.Text = "Allowed Addresses";
            // 
            // WhitelistManagementControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.whiteListLabel);
            this.Controls.Add(this.newAddressTextBox);
            this.Controls.Add(this.addressAddButton);
            this.Controls.Add(this.addressRemoveButton);
            this.Controls.Add(this.whitelistComboBox);
            this.Name = "WhitelistManagementControl";
            this.Size = new System.Drawing.Size(297, 139);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ComboBox whitelistComboBox;
        private System.Windows.Forms.Button addressRemoveButton;
        private System.Windows.Forms.Button addressAddButton;
        private System.Windows.Forms.TextBox newAddressTextBox;
        private System.Windows.Forms.Label whiteListLabel;
    }
}
