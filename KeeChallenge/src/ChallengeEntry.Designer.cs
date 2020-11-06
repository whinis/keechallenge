﻿namespace KeeChallenge
{
    partial class ChallengeEntry
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
            this.secretTextBox = new System.Windows.Forms.TextBox();
            this.promptLabel = new System.Windows.Forms.Label();
            this.OKButton = new System.Windows.Forms.Button();
            this.CancelButton1 = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // secretTextBox
            // 
            this.secretTextBox.Location = new System.Drawing.Point(22, 31);
            this.secretTextBox.MaxLength = 256;
            this.secretTextBox.Name = "secretTextBox";
            this.secretTextBox.PasswordChar = '*';
            this.secretTextBox.Size = new System.Drawing.Size(310, 20);
            this.secretTextBox.TabIndex = 2;
            // 
            // promptLabel
            // 
            this.promptLabel.AutoSize = true;
            this.promptLabel.Location = new System.Drawing.Point(137, 9);
            this.promptLabel.Name = "promptLabel";
            this.promptLabel.Size = new System.Drawing.Size(82, 13);
            this.promptLabel.TabIndex = 3;
            this.promptLabel.Text = "Enter Challenge";
            // 
            // OKButton
            // 
            this.OKButton.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.OKButton.Location = new System.Drawing.Point(22, 57);
            this.OKButton.Name = "OKButton";
            this.OKButton.Size = new System.Drawing.Size(75, 23);
            this.OKButton.TabIndex = 4;
            this.OKButton.Text = "&OK";
            this.OKButton.UseVisualStyleBackColor = true;
            this.OKButton.Click += new System.EventHandler(this.OKButton_Click);
            // 
            // CancelButton1
            // 
            this.CancelButton1.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.CancelButton1.Location = new System.Drawing.Point(257, 57);
            this.CancelButton1.Name = "CancelButton1";
            this.CancelButton1.Size = new System.Drawing.Size(75, 23);
            this.CancelButton1.TabIndex = 5;
            this.CancelButton1.Text = "&Cancel";
            this.CancelButton1.UseVisualStyleBackColor = true;
            // 
            // ChallengeEntry
            // 
            this.AcceptButton = this.OKButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(355, 86);
            this.Controls.Add(this.CancelButton1);
            this.Controls.Add(this.OKButton);
            this.Controls.Add(this.promptLabel);
            this.Controls.Add(this.secretTextBox);
            this.Name = "ChallengeEntry";
            this.Text = "User Challenge Entry";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox secretTextBox;
        private System.Windows.Forms.Label promptLabel;
        private System.Windows.Forms.Button CancelButton1;
        private System.Windows.Forms.Button OKButton;
    }
}