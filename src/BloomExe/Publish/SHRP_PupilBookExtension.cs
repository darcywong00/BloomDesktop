﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.ComponentModel.Composition;
using System.Security.Cryptography;
using System.Windows.Forms;
using System.Xml;
using Bloom.Book;
using System.Linq;
using Palaso.Xml;
using Palaso.IO;

namespace Bloom.Publish
{

// ReSharper disable once InconsistentNaming
	/// <summary>
	/// This class currently just does one thing; it adds a right-click menu item in the Publish tab that saved PNG thumbnails of all the "day" pages of a SHRP Pupil's book.
	/// These are used in the corresponding "Teacher's Guide".
	///
	/// It is built as an MEF "part", exporting what Bloom needs (the menu item) and importing what it needs (e.g. the contents of the book, an html thumbnailer)
	///
	/// Currently (In Dec 2013), it isn't actually an extension because it doesn't have its own DLL. But it does demonstrate that we could trivially have extension dlls coming with
	/// templates, in separate DLLS. The key is keeping the dependences to a minimum so that extension don't break with each version of Bloom.
	///
	/// </summary>
	public class SHRP_PupilBookExtension
	{

		public static bool ExtensionIsApplicable(Book.Book book)
		{
			return book.Title.Contains("Pupil") && book.GetDataItem("week") != null;
		}

		[Import("PathToBookFolder")]
		public string BookFolder;

		[Import("Language1Iso639Code")]
		public string Language1Iso639Code;

		[Import]
		public Action<int, int, HtmlDom, Action<Image>, Action<Exception>> GetThumbnailAsync;


		[Import] public Func<IEnumerable<HtmlDom>> GetPageDoms;

		[Export("GetPublishingMenuCommands")]
		public IEnumerable<ToolStripItem> GetPublishingMenuCommands()
		{
			yield return new ToolStripSeparator();
			yield return new ToolStripMenuItem("Make Thumbnails For Teacher's Guide", null, MakeThumbnailsForTeachersGuide);
		}

		private void MakeThumbnailsForTeachersGuide(object sender, EventArgs e)
		{
			var exportFolder = Path.Combine(BookFolder, "Thumbnails");

			if (Directory.Exists(exportFolder))
			{
				Directory.Delete(exportFolder, true);
			}
			Directory.CreateDirectory(exportFolder);
			foreach (var pageDom in GetPageDoms())
			{
				if (null != pageDom.SelectSingleNode("//div[contains(@class,'oddPage') or contains(@class,'evenPage')]"))
				{
					const double kproportionOfWidthToHeightForB5 = 0.708;
					const int heightInPixels = 700;
					const int widthInPixels = (int) (heightInPixels*kproportionOfWidthToHeightForB5);

					GetThumbnailAsync(widthInPixels, heightInPixels, pageDom, image => ThumbnailReady(exportFolder, pageDom, image),
						error => HandleThumbnailerError(pageDom, error));
				}
			}
			//this folder won't be fully populated yet, but as they watch it will fill up
			PathUtilities.OpenDirectoryInExplorer(exportFolder);
		}
		private void HandleThumbnailerError(HtmlDom pageDom, Exception error)
		{
			throw new NotImplementedException();
		}

		private void ThumbnailReady(string exportFolder, HtmlDom dom, Image image)
		{
			var term = dom.SelectSingleNode("//div[contains(@data-book,'term')]").InnerText.Trim();
			var week = dom.SelectSingleNode("//div[contains(@data-book,'week')]").InnerText.Trim();
			//the selector for day one is different because it doesn't have @data-* attribute
			XmlElement dayNode = dom.SelectSingleNode("//div[contains(@class,'DayStyle')]");
			string page="?";
			// many pupil books don't have a specific day per page
			if (dayNode != null)
			{
				page = dayNode.InnerText.Trim();
			}
			else
			{
				if (dom.SelectSingleNode("//div[contains(@class,'page1')]") != null)
				{
					page = "1";
				}
				else if (dom.SelectSingleNode("//div[contains(@class,'page2')]") != null)
				{
					page = "2";
				}
				else if (dom.SelectSingleNode("//div[contains(@class,'page3')]") != null)
				{
					page = "3";
				}
				else if (dom.SelectSingleNode("//div[contains(@class,'page4')]") != null)
				{
					page = "4";
				}
				else
				{
					Debug.Fail("Couldn't figure out what page this is.");
				}
			}
			var fileName = Language1Iso639Code + "-t" + term + "-w" + week + "-p" + page + ".png";
			//just doing image.Save() works for .bmp and .jpg, but not .png
			using (var b = new Bitmap(image))
			{
				b.Save(Path.Combine(exportFolder, fileName));
			}
		}
	}
}
