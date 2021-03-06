﻿using System;
using System.IO;
using System.Linq;
using Bloom.web;
using Palaso.IO;
using Palaso.Reporting;
using Bloom.Properties;

namespace Bloom.ImageProcessing
{
	/// <summary>
	/// This is a local http server which just serves up images. Its job is to take the original image
	/// and lower it to screen resolution, because gecko was having so much trouble dealing with hi-res
	/// images intended for print publications. While this could have been accomplished just making the
	/// img src attribute point to an alternate destination on disk, I did it this way so that we can
	/// generate lo-res images in an asynchronous fashion, which will degrade nicely on slower machines.
	/// That is, the browser is happy to show the picture later, when it is ready, if it is comming from
	/// an http request. In constrast, a file:// is just there or not there... no async about it.
	///
	///Hints
	/// To check what's in the url access control list on Vista and up: netsh http show urlacl
	///on XP: httpcfg query urlacl
	///
	///nb: had trouble with 8080. Remember to enable this with (windows 7 up): netsh http add urlacl url=http://localhost:8089/bloom user=everyone
	///on Windows XP, use httpcfg. I haven't tested this, but I think it may be: HTTPCFG set urlacl -u http://+:8089/bloom/ /a D:(A;;GX;;;WD)
	/// </summary>
	public class ImageServer : ServerBase
	{
		private RuntimeImageProcessor _cache;
		private bool _useCache;

		public ImageServer(RuntimeImageProcessor cache)
		{
			_cache = cache;
			_useCache = Settings.Default.ImageHandler != "off";
		}

		protected override void Dispose(bool fDisposing)
		{
			if (fDisposing)
			{
				if (_cache != null)
					_cache.Dispose();
				_cache = null;
			}

			base.Dispose(fDisposing);
		}

		protected override void StartWithSetupIfNeeded()
		{
			try
			{
				base.StartWithSetupIfNeeded();
			}
			catch (Exception error)
			{
				var e = new ApplicationException("Could not start ImageServer", error);//passing this in will enable the details button
				ErrorReport.NotifyUserOfProblem(e, "What Happened{0}" +
					"Bloom could not start its local file server, and cannot work properly without it.{0}{0}" +
					"What caused this?{0}" +
					"Possibly another version of Bloom is running, perhaps not very obviously. " +
					"Possibly Bloom does not know how to get your specific {1} operating system to allow its image server to run.{0}{0}" +
					"What can you do?{0}" +
					"Click OK, then exit Bloom and restart your computer.{0}" +
					"If the problem keeps happening, click 'Details' and report the problem to the developers.", Environment.NewLine,
					Palaso.PlatformUtilities.Platform.IsWindows ? "Windows" : "Linux");
			}
		}

		protected override bool ProcessRequest(IRequestInfo info)
		{
			if (base.ProcessRequest(info))
				return true;

			if (!_useCache)
				return false;

			var r = GetLocalPathWithoutQuery(info);

			// only process images
			var isSvg = r.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
			if (!IsImageTypeThatCanBeDegraded(r) && !isSvg)
				return false;

			r = r.Replace("thumbnail", "");

			// This happens with the new way we are serving css files
			if (!File.Exists(r))
			{
				var fileName = Path.GetFileName(r);
				var sourceDir = FileLocator.GetDirectoryDistributedWithApplication("BloomBrowserUI");
				r = Directory.EnumerateFiles(sourceDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
			}

			if (!string.IsNullOrEmpty(r))
			{
				if (isSvg)
				{
					info.ReplyWithImage(r);
					return true;
				}

				// thumbnail requests have the thumbnail parameter set in the query string
				var thumb = info.GetQueryString()["thumbnail"] != null;
				var pathToFile = _cache.GetPathToResizedImage(r, thumb);

				if (string.IsNullOrEmpty(pathToFile)) return false;

				info.ReplyWithImage(pathToFile);
				return true;
			}
			return false;
		}

		protected static bool IsImageTypeThatCanBeDegraded(string path)
		{
			var extension = Path.GetExtension(path);
			if(!string.IsNullOrEmpty(extension))
				extension = extension.ToLower();
			//note, we're omitting SVG
			return (new[] { ".png", ".jpg", ".jpeg"}.Contains(extension));
		}
	}
}
