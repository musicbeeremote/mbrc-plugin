namespace MusicBeePlugin
{
    partial class SettingsPanel
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
            this.connectionSettingsCategoryLabel = new System.Windows.Forms.Label();
            this.seperator1 = new System.Windows.Forms.Label();
            this.portLabel = new System.Windows.Forms.Label();
            this.portNumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.addressFilteringCategoryLabel = new System.Windows.Forms.Label();
            this.seperator2 = new System.Windows.Forms.Label();
            this.selectionFilteringComboBox = new System.Windows.Forms.ComboBox();
            this.allowLabel = new System.Windows.Forms.Label();
            this.ipAddressInputTextBox = new System.Windows.Forms.TextBox();
            this.addressLabel = new System.Windows.Forms.Label();
            this.rangeNumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.dashLabel = new System.Windows.Forms.Label();
            this.addAddressButton = new System.Windows.Forms.Button();
            this.removeAddressButton = new System.Windows.Forms.Button();
            this.allowedAddressesComboBox = new System.Windows.Forms.ComboBox();
            this.allowedLabel = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.portNumericUpDown)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.rangeNumericUpDown)).BeginInit();
            this.SuspendLayout();
            // 
            // connectionSettingsCategoryLabel
            // 
            this.connectionSettingsCategoryLabel.AutoSize = true;
            this.connectionSettingsCategoryLabel.Location = new System.Drawing.Point(4, 4);
            this.connectionSettingsCategoryLabel.Name = "connectionSettingsCategoryLabel";
            this.connectionSettingsCategoryLabel.Size = new System.Drawing.Size(102, 13);
            this.connectionSettingsCategoryLabel.TabIndex = 0;
            this.connectionSettingsCategoryLabel.Text = "Connection Settings";
            // 
            // seperator1
            // 
            this.seperator1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.seperator1.Location = new System.Drawing.Point(3, 18);
            this.seperator1.Name = "seperator1";
            this.seperator1.Size = new System.Drawing.Size(250, 1);
            this.seperator1.TabIndex = 1;
            // 
            // portLabel
            // 
            this.portLabel.AutoSize = true;
            this.portLabel.Location = new System.Drawing.Point(10, 27);
            this.portLabel.Name = "portLabel";
            this.portLabel.Size = new System.Drawing.Size(29, 13);
            this.portLabel.TabIndex = 2;
            this.portLabel.Text = "Port:";
            // 
            // portNumericUpDown
            // 
            this.portNumericUpDown.Location = new System.Drawing.Point(95, 25);
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
            this.portNumericUpDown.Size = new System.Drawing.Size(108, 20);
            this.portNumericUpDown.TabIndex = 3;
            this.portNumericUpDown.Value = new decimal(new int[] {
            3000,
            0,
            0,
            0});
            // 
            // addressFilteringCategoryLabel
            // 
            this.addressFilteringCategoryLabel.AutoSize = true;
            this.addressFilteringCategoryLabel.Location = new System.Drawing.Point(4, 50);
            this.addressFilteringCategoryLabel.Name = "addressFilteringCategoryLabel";
            this.addressFilteringCategoryLabel.Size = new System.Drawing.Size(84, 13);
            this.addressFilteringCategoryLabel.TabIndex = 4;
            this.addressFilteringCategoryLabel.Text = "Address Filtering";
            // 
            // seperator2
            // 
            this.seperator2.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.seperator2.Location = new System.Drawing.Point(3, 64);
            this.seperator2.Name = "seperator2";
            this.seperator2.Size = new System.Drawing.Size(250, 1);
            this.seperator2.TabIndex = 5;
            // 
            // selectionFilteringComboBox
            // 
            this.selectionFilteringComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.selectionFilteringComboBox.FormattingEnabled = true;
            this.selectionFilteringComboBox.Items.AddRange(new object[] {
            "All",
            "Range",
            "Specified"});
            this.selectionFilteringComboBox.Location = new System.Drawing.Point(95, 72);
            this.selectionFilteringComboBox.Name = "selectionFilteringComboBox";
            this.selectionFilteringComboBox.Size = new System.Drawing.Size(108, 21);
            this.selectionFilteringComboBox.TabIndex = 6;
            // 
            // allowLabel
            // 
            this.allowLabel.AutoSize = true;
            this.allowLabel.Location = new System.Drawing.Point(10, 75);
            this.allowLabel.Name = "allowLabel";
            this.allowLabel.Size = new System.Drawing.Size(35, 13);
            this.allowLabel.TabIndex = 7;
            this.allowLabel.Text = "Allow:";
            // 
            // ipAddressInputTextBox
            // 
            this.ipAddressInputTextBox.Location = new System.Drawing.Point(95, 99);
            this.ipAddressInputTextBox.Name = "ipAddressInputTextBox";
            this.ipAddressInputTextBox.Size = new System.Drawing.Size(108, 20);
            this.ipAddressInputTextBox.TabIndex = 8;
            // 
            // addressLabel
            // 
            this.addressLabel.AutoSize = true;
            this.addressLabel.Location = new System.Drawing.Point(10, 102);
            this.addressLabel.Name = "addressLabel";
            this.addressLabel.Size = new System.Drawing.Size(48, 13);
            this.addressLabel.TabIndex = 9;
            this.addressLabel.Text = "Address:";
            // 
            // rangeNumericUpDown
            // 
            this.rangeNumericUpDown.Location = new System.Drawing.Point(225, 100);
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
            this.rangeNumericUpDown.TabIndex = 10;
            this.rangeNumericUpDown.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // dashLabel
            // 
            this.dashLabel.AutoSize = true;
            this.dashLabel.Location = new System.Drawing.Point(209, 102);
            this.dashLabel.Name = "dashLabel";
            this.dashLabel.Size = new System.Drawing.Size(10, 13);
            this.dashLabel.TabIndex = 11;
            this.dashLabel.Text = "-";
            // 
            // addAddressButton
            // 
            this.addAddressButton.Location = new System.Drawing.Point(225, 125);
            this.addAddressButton.Name = "addAddressButton";
            this.addAddressButton.Size = new System.Drawing.Size(21, 21);
            this.addAddressButton.TabIndex = 0;
            this.addAddressButton.Text = "+";
            this.addAddressButton.UseVisualStyleBackColor = true;
            // 
            // removeAddressButton
            // 
            this.removeAddressButton.Location = new System.Drawing.Point(247, 125);
            this.removeAddressButton.Name = "removeAddressButton";
            this.removeAddressButton.Size = new System.Drawing.Size(21, 21);
            this.removeAddressButton.TabIndex = 1;
            this.removeAddressButton.Text = "-";
            this.removeAddressButton.UseVisualStyleBackColor = true;
            // 
            // allowedAddressesComboBox
            // 
            this.allowedAddressesComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.allowedAddressesComboBox.FormattingEnabled = true;
            this.allowedAddressesComboBox.Location = new System.Drawing.Point(95, 125);
            this.allowedAddressesComboBox.Name = "allowedAddressesComboBox";
            this.allowedAddressesComboBox.Size = new System.Drawing.Size(108, 21);
            this.allowedAddressesComboBox.TabIndex = 2;
            // 
            // allowedLabel
            // 
            this.allowedLabel.AutoSize = true;
            this.allowedLabel.Location = new System.Drawing.Point(10, 128);
            this.allowedLabel.Name = "allowedLabel";
            this.allowedLabel.Size = new System.Drawing.Size(47, 13);
            this.allowedLabel.TabIndex = 14;
            this.allowedLabel.Text = "Allowed:";
            // 
            // SettingsPanel
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.dashLabel);
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
            this.Name = "SettingsPanel";
            this.Size = new System.Drawing.Size(377, 200);
            ((System.ComponentModel.ISupportInitialize)(this.portNumericUpDown)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.rangeNumericUpDown)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label connectionSettingsCategoryLabel;
        private System.Windows.Forms.Label seperator1;
        private System.Windows.Forms.Label portLabel;
        private System.Windows.Forms.NumericUpDown portNumericUpDown;
        private System.Windows.Forms.Label addressFilteringCategoryLabel;
        private System.Windows.Forms.Label seperator2;
        private System.Windows.Forms.ComboBox selectionFilteringComboBox;
        private System.Windows.Forms.Label allowLabel;
        private System.Windows.Forms.TextBox ipAddressInputTextBox;
        private System.Windows.Forms.Label addressLabel;
        private System.Windows.Forms.NumericUpDown rangeNumericUpDown;
        private System.Windows.Forms.Label dashLabel;
        private System.Windows.Forms.Button addAddressButton;
        private System.Windows.Forms.Button removeAddressButton;
        private System.Windows.Forms.ComboBox allowedAddressesComboBox;
        private System.Windows.Forms.Label allowedLabel;
    }
}
