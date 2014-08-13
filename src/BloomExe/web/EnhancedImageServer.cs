﻿// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using Bloom.ImageProcessing;
using System.IO;
using Palaso.IO;

namespace Bloom.web
{
	/// <summary>
	/// A local http server that can serve (low-res) images plus other files.
	/// </summary>
	public class EnhancedImageServer: ImageServer
	{
		public EnhancedImageServer(LowResImageCache cache): base(cache)
		{
		}

		public string CurrentPageContent { get; set; }
		public string AccordionContent { get; set; }

		protected override bool ProcessRequest(IRequestInfo info)
		{
			if (base.ProcessRequest(info))
				return true;

			var localPath = GetLocalPathWithoutQuery(info);
			if (localPath == "currentPageContent")
			{
				info.ContentType = "text/html";
				info.WriteCompleteOutput(CurrentPageContent ?? "");
				return true;
			}
			else if (localPath == "accordionContent")
			{
				info.ContentType = "text/html";
				info.WriteCompleteOutput(AccordionContent ?? "");
				return true;
			}
			string path = null;
			try
			{
				path = FileLocator.GetFileDistributedWithApplication("BloomBrowserUI", localPath);
			}
			catch (ApplicationException)
			{
				// ignore
			}
			if (File.Exists(path))
			{
				info.ContentType = GetContentType(Path.GetExtension(localPath));

				info.ReplyWithFileContent(path);
				return true;
			}
			return false;
		}
	}
}