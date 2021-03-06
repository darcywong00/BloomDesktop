﻿namespace Bloom.Collection.BloomPack
{
	partial class BloomPackInstallDialog
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
			System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(BloomPackInstallDialog));
			this.pictureBox1 = new System.Windows.Forms.PictureBox();
			this._okButton = new System.Windows.Forms.Button();
			this._message = new Palaso.UI.WindowsForms.Widgets.BetterLabel();
			this._errorImage = new System.Windows.Forms.PictureBox();
			this._backgroundWorker = new System.ComponentModel.BackgroundWorker();
			this._startupTimer = new System.Windows.Forms.Timer(this.components);
			this._L10NSharpExtender = new L10NSharp.UI.L10NSharpExtender(this.components);
			((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this._errorImage)).BeginInit();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).BeginInit();
			this.SuspendLayout();
			// 
			// pictureBox1
			// 
			this.pictureBox1.Image = global::Bloom.Properties.Resources.BloomPack64x64;
			this._L10NSharpExtender.SetLocalizableToolTip(this.pictureBox1, null);
			this._L10NSharpExtender.SetLocalizationComment(this.pictureBox1, null);
			this._L10NSharpExtender.SetLocalizingId(this.pictureBox1, "BloomPackInstallDialog.pictureBox1");
			this.pictureBox1.Location = new System.Drawing.Point(12, 12);
			this.pictureBox1.Name = "pictureBox1";
			this.pictureBox1.Size = new System.Drawing.Size(67, 68);
			this.pictureBox1.TabIndex = 0;
			this.pictureBox1.TabStop = false;
			// 
			// _okButton
			// 
			this._L10NSharpExtender.SetLocalizableToolTip(this._okButton, null);
			this._L10NSharpExtender.SetLocalizationComment(this._okButton, null);
			this._L10NSharpExtender.SetLocalizingId(this._okButton, "Common.OKButton");
			this._okButton.Location = new System.Drawing.Point(317, 126);
			this._okButton.Name = "_okButton";
			this._okButton.Size = new System.Drawing.Size(75, 23);
			this._okButton.TabIndex = 1;
			this._okButton.Text = "&OK";
			this._okButton.UseVisualStyleBackColor = true;
			this._okButton.Click += new System.EventHandler(this._okButton_Click);
			// 
			// _message
			// 
			this._message.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
			this._message.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this._message.Enabled = false;
			this._message.Font = new System.Drawing.Font("Segoe UI", 9F);
			this._message.ForeColor = System.Drawing.SystemColors.ControlText;
			this._L10NSharpExtender.SetLocalizableToolTip(this._message, null);
			this._L10NSharpExtender.SetLocalizationComment(this._message, null);
			this._L10NSharpExtender.SetLocalizingId(this._message, "BloomPackInstallDialog._message");
			this._message.Location = new System.Drawing.Point(96, 12);
			this._message.Multiline = true;
			this._message.Name = "_message";
			this._message.ReadOnly = true;
			this._message.Size = new System.Drawing.Size(267, 0);
			this._message.TabIndex = 2;
			this._message.TabStop = false;
			// 
			// _errorImage
			// 
			this._errorImage.Image = global::Bloom.Properties.Resources.Error70x70;
			this._L10NSharpExtender.SetLocalizableToolTip(this._errorImage, null);
			this._L10NSharpExtender.SetLocalizationComment(this._errorImage, null);
			this._L10NSharpExtender.SetLocalizingId(this._errorImage, "BloomPackInstallDialog._errorImage");
			this._errorImage.Location = new System.Drawing.Point(12, 106);
			this._errorImage.Name = "_errorImage";
			this._errorImage.Size = new System.Drawing.Size(42, 43);
			this._errorImage.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
			this._errorImage.TabIndex = 3;
			this._errorImage.TabStop = false;
			this._errorImage.Visible = false;
			// 
			// _backgroundWorker
			// 
			this._backgroundWorker.WorkerReportsProgress = true;
			this._backgroundWorker.DoWork += new System.ComponentModel.DoWorkEventHandler(this._backgroundWorker_DoWork);
			this._backgroundWorker.ProgressChanged += new System.ComponentModel.ProgressChangedEventHandler(this._backgroundWorker_ProgressChanged);
			this._backgroundWorker.RunWorkerCompleted += new System.ComponentModel.RunWorkerCompletedEventHandler(this._backgroundWorker_RunWorkerCompleted);
			// 
			// _startupTimer
			// 
			this._startupTimer.Enabled = true;
			this._startupTimer.Tick += new System.EventHandler(this._startupTimer_Tick);
			// 
			// _L10NSharpExtender
			// 
			this._L10NSharpExtender.LocalizationManagerId = "Bloom";
			this._L10NSharpExtender.PrefixForNewItems = "BloomPackInstallDialog";
			// 
			// BloomPackInstallDialog
			// 
			this.AcceptButton = this._okButton;
			this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.ClientSize = new System.Drawing.Size(422, 172);
			this.Controls.Add(this._errorImage);
			this.Controls.Add(this._message);
			this.Controls.Add(this._okButton);
			this.Controls.Add(this.pictureBox1);
			this._L10NSharpExtender.SetLocalizableToolTip(this, null);
			this._L10NSharpExtender.SetLocalizationComment(this, null);
			this._L10NSharpExtender.SetLocalizingId(this, "BloomPackInstallDialog.BloomPackInstallation");
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.Name = "BloomPackInstallDialog";
			this.Text = "Bloom Pack Installation";
			((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this._errorImage)).EndInit();
			((System.ComponentModel.ISupportInitialize)(this._L10NSharpExtender)).EndInit();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.PictureBox pictureBox1;
		private System.Windows.Forms.Button _okButton;
		private Palaso.UI.WindowsForms.Widgets.BetterLabel _message;
		private System.Windows.Forms.PictureBox _errorImage;
		private System.ComponentModel.BackgroundWorker _backgroundWorker;
		private System.Windows.Forms.Timer _startupTimer;
		private L10NSharp.UI.L10NSharpExtender _L10NSharpExtender;
	}
}