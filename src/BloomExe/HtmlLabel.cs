﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Gecko;
using Palaso.IO;
using Palaso.UI.WindowsForms.Extensions;

namespace Bloom
{
	/// <summary>
	/// Any links web links will open in the default bowser.
	/// For file:/// links: The path will be extracted and handed to the OS to open,
	/// unless there is a class "showFileLocation", in which case we attempt to open a file explorer and select the file.
	/// </summary>
	public partial class HtmlLabel : UserControl
	{
		private GeckoWebBrowser _browser;
		private string _html;

		public HtmlLabel()
		{
			InitializeComponent();

			if (this.DesignModeAtAll())
			{
				return;
			}

			_browser = new GeckoWebBrowser();

			_browser.Parent = this;
			_browser.Dock = DockStyle.Fill;
			Controls.Add(_browser);
			_browser.NoDefaultContextMenu = true;
			_browser.Margin = new Padding(0);
		}

		/// <summary>
		/// Just a simple html string, no html, head, body tags.
		/// </summary>
		[Browsable(true), CategoryAttribute("Text")]
		public string HTML
		{
			get { return _html; }
			set
			{
				_html = value;
				if (this.DesignModeAtAll())
					return;

				if (_browser != null)
				{
					_browser.Visible = !string.IsNullOrEmpty(_html);
					var htmlColor = ColorTranslator.ToHtml(ForeColor);
					var backgroundColor = ColorTranslator.ToHtml(BackColor);
					if (_browser.Visible)
					{
						var s = "<!DOCTYPE html><html><head><meta charset=\"UTF-8\"></head><body style=\"background-color: " +
								backgroundColor +
								"\"><span style=\"color:" + htmlColor + "; font-family:Segoe UI, Arial; font-size:" + Font.Size.ToString() +
								"pt\">" + _html + "</span></body></html>";
						_browser.LoadHtml(s);
					}
				}
			}
		}

		private void HtmlLabel_Load(object sender, EventArgs e)
		{
			if (this.DesignModeAtAll())
				return;

			HTML = _html;//in the likely case that there's html waiting to be shown
			_browser.DomClick += new EventHandler<DomMouseEventArgs>(OnBrowser_DomClick);
		}

		private void OnBrowser_DomClick(object sender, DomEventArgs ge)
		{
			if (this.DesignModeAtAll())
				return;

			if (ge.Target == null)
				return;
			var element = ge.Target.CastToGeckoElement();
			if (element.TagName == "A")
			{
				var url = element.GetAttribute("href");
				if (url.StartsWith("file://"))
				{
					var path = url.Replace("file://", "");

					var classAttr = element.GetAttribute("class");
					if (classAttr != null && classAttr.Contains("showFileLocation"))
					{
						PathUtilities.SelectFileInExplorer(path);
					}
					else
					{
						Process.Start(path);
					}
				}
				else
				{
					Process.Start(url);
				}
				ge.Handled = true; //don't let the browser navigate itself
			}
		}
	}
}