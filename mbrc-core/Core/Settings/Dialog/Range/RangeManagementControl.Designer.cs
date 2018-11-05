namespace MusicBeeRemote.Core.Settings.Dialog.Range
{
    partial class RangeManagementControl
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
            this.toLabel = new System.Windows.Forms.Label();
            this.rangeLabel = new System.Windows.Forms.Label();
            this.baseIpTextBox = new MusicBeeRemote.Core.Settings.Dialog.HintTextBox();
            this.lastOctetTextBox = new MusicBeeRemote.Core.Settings.Dialog.HintTextBox();
            this.SuspendLayout();
            // 
            // toLabel
            // 
            this.toLabel.AutoSize = true;
            this.toLabel.Location = new System.Drawing.Point(152, 41);
            this.toLabel.Name = "toLabel";
            this.toLabel.Size = new System.Drawing.Size(23, 20);
            this.toLabel.TabIndex = 2;
            this.toLabel.Text = "to";
            // 
            // rangeLabel
            // 
            this.rangeLabel.AutoSize = true;
            this.rangeLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 8F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.rangeLabel.Location = new System.Drawing.Point(3, 12);
            this.rangeLabel.Name = "rangeLabel";
            this.rangeLabel.Size = new System.Drawing.Size(84, 20);
            this.rangeLabel.TabIndex = 3;
            this.rangeLabel.Text = "IP Range";
            // 
            // baseIpTextBox
            // 
            this.baseIpTextBox.Hint = "192.168.1.10";
            this.baseIpTextBox.Location = new System.Drawing.Point(7, 38);
            this.baseIpTextBox.Name = "baseIpTextBox";
            this.baseIpTextBox.Size = new System.Drawing.Size(139, 26);
            this.baseIpTextBox.TabIndex = 4;
            this.baseIpTextBox.TextChanged += new System.EventHandler(this.BaseIpTextBox_TextChanged);
            // 
            // lastOctetTextBox
            // 
            this.lastOctetTextBox.Hint = "110";
            this.lastOctetTextBox.Location = new System.Drawing.Point(181, 38);
            this.lastOctetTextBox.Name = "lastOctetTextBox";
            this.lastOctetTextBox.Size = new System.Drawing.Size(42, 26);
            this.lastOctetTextBox.TabIndex = 5;
            this.lastOctetTextBox.TextChanged += new System.EventHandler(this.LastOctetTextBox_TextChanged);
            // 
            // RangeManagementControl
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.lastOctetTextBox);
            this.Controls.Add(this.baseIpTextBox);
            this.Controls.Add(this.rangeLabel);
            this.Controls.Add(this.toLabel);
            this.Name = "RangeManagementControl";
            this.Size = new System.Drawing.Size(275, 77);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Label toLabel;
        private System.Windows.Forms.Label rangeLabel;
        private MusicBeeRemote.Core.Settings.Dialog.HintTextBox baseIpTextBox;
        private MusicBeeRemote.Core.Settings.Dialog.HintTextBox lastOctetTextBox;
    }
}
