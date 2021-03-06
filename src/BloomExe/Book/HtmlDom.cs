﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Xsl;
using System.Linq;
using Palaso.Extensions;
using Palaso.Reporting;
using Palaso.Xml;

namespace Bloom.Book
{
	/// <summary>
	/// HtmlDom manages the lower-level operations on a Bloom XHTML DOM.
	/// These doms can be a whole book, or just one page we're currently editing.
	/// They are actually XHTML, though when we save or send to a browser, we always convert to plain html.
	/// May also contain a BookInfo, which for certain operations should be kept in sync with the HTML.
	/// </summary>
	public class HtmlDom
	{
		public const string RelativePathAttrName = "data-base";
		private XmlDocument _dom;

		public HtmlDom()
		{
			_dom = new XmlDocument();
			_dom.LoadXml("<html><head></head><body></body></html>");
		}

		public HtmlDom(XmlDocument domToClone)
		{
			_dom = (XmlDocument) domToClone.Clone();
		}

		public HtmlDom(string xhtml)
		{
			_dom = new XmlDocument();
			_dom.LoadXml(xhtml);
		}

		public XmlElement Head
		{
			get { return XmlUtils.GetOrCreateElement(_dom, "html", "head"); }
		}
		public XmlElement Body
		{
			get { return XmlUtils.GetOrCreateElement(_dom, "html", "body"); }
		}
		public string Title
		{
			get
			{
				return XmlUtils.GetTitleOfHtml(_dom, null);
				;
			}
			set
			{
				var t = value.Trim();
				//if (!String.IsNullOrEmpty(t))
				//{
					var makeSureItsThere = Head;
					var titleNode = XmlUtils.GetOrCreateElement(_dom, "html/head", "title");
					//ah, but maybe that contains html element in there, like <br/> where the user typed a return in the title,

					//so we set the xhtml (not the text) of the node
					titleNode.InnerXml = t;
					//then ask it for the text again (will drop the xhtml)
					var justTheText = titleNode.InnerText.Replace("\r\n", " ").Replace("\n", " ").Replace("  ", " ");
					//then clear it
					titleNode.InnerXml = "";
					//and set the text again!
					titleNode.InnerText = justTheText;
				//}
			}
		}

		public XmlDocument RawDom
		{
			get { return _dom; }
		}

		public string InnerXml
		{
			get { return _dom.InnerXml; }
		}

		public HtmlDom Clone()
		{
			return new HtmlDom(RawDom);
		}

		public void UpdatePageDivs()
		{
			//add a unique id for our use
			//review: bookstarter sticks in the ids, this one updates (and skips if it it didn't have an id before). At a minimum, this needs explanation
			foreach (XmlElement node in _dom.SafeSelectNodes("/html/body/div"))
			{
				//in the beta, 0.8, the ID of the page in the front-matter template was used for the 1st
				//page of every book. This screws up thumbnail caching.
				const string guidMistakenlyUsedForEveryCoverPage = "74731b2d-18b0-420f-ac96-6de20f659810";
				if (String.IsNullOrEmpty(node.GetAttribute("id"))
					|| (node.GetAttribute("id") == guidMistakenlyUsedForEveryCoverPage))
					node.SetAttribute("id", Guid.NewGuid().ToString());
			}
		}

		private string _baseForRelativePaths = null;

		/// <summary>
		/// This property records the folder in which the browser needs to find files referred to using
		/// non-absolute locations.
		/// This method is designed to be used in conjunction with EnhancedImageServer.MakeSimulatedPageFileInBookFolder().
		/// which generates URLs that give the browser the content of this DOM, and also handles derived urls
		/// relative to that one.
		/// </summary>
		/// <remarks>Originally, this method created a 'base' element in the DOM, and a real
		/// temporary file would typically be created. The base element caused the browser to
		/// redirect things in much the way described above. However, this strategy fails
		/// for internal links within the document: a url like #mybookmark is translated
		/// into localhost://c:/users/someone/bloom/mycollection/mybookfolder#mybookmark, with no
		/// document specified at all, and passed to the server, which fails to find anything.
		/// Later it was discovered that Configurator (for Wall Calendar) put in a 'base' element,
		/// so we still need the parts that remove any 'base' element.</remarks>
		public string BaseForRelativePaths
		{
			get { return _baseForRelativePaths; }
			set
			{
				var path = value;
				_baseForRelativePaths = path ?? string.Empty;
				var head = _dom.SelectSingleNodeHonoringDefaultNS("//head");
				if (head == null)
					return;
				foreach (XmlNode baseNode in head.SafeSelectNodes("base"))
				{
					head.RemoveChild(baseNode);
				}
			}
		}

		/// <summary>
		/// Set this for DOMs that should not get the on-screen enhancements (transparency, possibly compression)
		/// of images. Typically for generating print-quality PDFs.
		/// </summary>
		internal bool UseOriginalImages { get; set; }


		public void AddStyleSheet(string locateFile)
		{
			RawDom.AddStyleSheet(locateFile);
		}

		public XmlNodeList SafeSelectNodes(string xpath)
		{
			return RawDom.SafeSelectNodes(xpath);
		}

		public XmlElement SelectSingleNode(string xpath)
		{
			return RawDom.SelectSingleNode(xpath) as XmlElement;
		}

		public XmlElement SelectSingleNodeHonoringDefaultNS(string xpath)
		{
			return _dom.SelectSingleNodeHonoringDefaultNS(xpath) as XmlElement;
		}

		public void AddJavascriptFile(string pathToJavascript)
		{
			Head.AppendChild(MakeJavascriptElement(pathToJavascript));
		}

		private XmlElement MakeJavascriptElement(string pathToJavascript)
		{
			XmlElement element = Head.AppendChild(_dom.CreateElement("script")) as XmlElement;

			element.IsEmpty = false;
			element.SetAttribute("type", "text/javascript");
			element.SetAttribute("src", pathToJavascript.ToLocalhost());
			return element;
		}

		public void AddJavascriptFileToBody(string pathToJavascript)
		{
			Body.AppendChild(MakeJavascriptElement(pathToJavascript));
		}

		/// <summary>
		/// The Creation Type is either "translation" or "original". This is used to protect fields that should
		/// normally not be editable in one or the other.
		/// This is a bad name, and we know it!
		/// </summary>
		public void AddCreationType(string mode)
		{
			// RemoveModeStyleSheets() should have already removed any editMode attribute on the body element
			Body.SetAttribute("bookcreationtype", mode);
		}

		public void RemoveModeStyleSheets()
		{
			foreach (XmlElement linkNode in RawDom.SafeSelectNodes("/html/head/link"))
			{
				var href = linkNode.GetAttribute("href");
				if (string.IsNullOrEmpty(href))
				{
					continue;
				}

				var fileName = Path.GetFileName(href);
				if (fileName.Contains("edit") || fileName.Contains("preview"))
				{
					linkNode.ParentNode.RemoveChild(linkNode);
				}
			}
			// If present, remove the editMode attribute that tells use which mode we're editing in (original or translation)
			var body = RawDom.SafeSelectNodes("/html/body")[0] as XmlElement;
			if (body.HasAttribute("editMode"))
				body.RemoveAttribute("editMode");
		}

		public string ValidateBook(string descriptionOfBookForErrorLog)
		{
			var ids = new List<string>();
			var builder = new StringBuilder();

			Ensure(RawDom.SafeSelectNodes("//div[contains(@class,'bloom-page')]").Count > 0, "Must have at least one page",
				   builder);
			EnsureIdsAreUnique(this, "textarea", ids, builder);
			EnsureIdsAreUnique(this, "p", ids, builder);
			EnsureIdsAreUnique(this, "img", ids, builder);

			//TODO: validate other things, including html
			var x = builder.ToString().Trim();
			if (x.Length == 0)
				Logger.WriteEvent("HtmlDom.ValidateBook({0}): No Errors", descriptionOfBookForErrorLog);
			else
			{
				Logger.WriteEvent("HtmlDom.ValidateBook({0}): {1}", descriptionOfBookForErrorLog, x);
			}

			return builder.ToString();
		}


		private static void Ensure(bool passes, string message, StringBuilder builder)
		{
			if (!passes)
				builder.AppendLine(message);
		}

		private static void EnsureIdsAreUnique(HtmlDom dom, string elementTag, List<string> ids, StringBuilder builder)
		{
			foreach (XmlElement element in dom.SafeSelectNodes("//" + elementTag + "[@id]"))
			{
				var id = element.GetAttribute("id");
				if (ids.Contains(id))
					builder.AppendLine("The id of this " + elementTag + " must be unique, but is not: " + element.OuterXml);
				else
					ids.Add(id);
			}
		}

		public void SortStyleSheetLinks()
		{
			List<XmlElement> links = new List<XmlElement>();
			foreach (XmlElement link in SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				links.Add(link);
			}
			if (links.Count < 2)
				return;

			var headNode = links[0].ParentNode;

			//clear them out
			foreach (var xmlElement in links)
			{
				headNode.RemoveChild(xmlElement);
			}

			links.Sort(new StyleSheetLinkSorter());

			//add them back
			foreach (var xmlElement in links)
			{
				headNode.AppendChild(xmlElement);
			}
		}

		/// <summary>
		/// gecko 11 requires the file://, but modern firefox and chrome can't handle it. Checked also that IE10 works without it.
		/// </summary>
		public void RemoveFileProtocolFromStyleSheetLinks()
		{
			List<XmlElement> links = new List<XmlElement>();
			foreach (XmlElement link in SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var linke = link.GetAttribute("href");
				link.SetAttribute("href", linke.Replace("file:///", "").Replace("file://", ""));
			}
		}

		public static void AddClass(XmlElement e, string className)
		{
			e.SetAttribute("class", (e.GetAttribute("class").Replace(className,"").Trim() + " " + className).Trim());
		}

		public static void AddRtlDir(XmlElement e)
		{
			e.SetAttribute("dir", "rtl");
		}

		public static void RemoveRtlDir(XmlElement e)
		{
			e.RemoveAttribute("dir");
		}

		public static void RemoveClassesBeginingWith(XmlElement xmlElement, string classPrefix)
		{

			var classes = xmlElement.GetAttribute("class");
			var original = classes;

			if (String.IsNullOrEmpty(classes))
				return;
			var parts = classes.SplitTrimmed(' ');

			classes = "";
			foreach (var part in parts)
			{
				if (!part.StartsWith(classPrefix))
					classes += part + " ";
			}
			xmlElement.SetAttribute("class", classes.Trim());

			//	Debug.WriteLine("RemoveClassesBeginingWith    " + xmlElement.InnerText+"     |    "+original + " ---> " + classes);
		}


		public static void AddClassIfMissing(XmlElement element, string className)
		{
			string classes = element.GetAttribute("class");
			if (classes.Contains(className))
				return;
			element.SetAttribute("class", (classes + " " + className).Trim());
		}


		/// <summary>
		/// Applies the XSLT, and returns an XML dom
		/// </summary>
		public XmlDocument ApplyXSLT(string pathToXSLT)
		{
			var transform = new XslCompiledTransform();
			transform.Load(pathToXSLT);
			using (var stringWriter = new StringWriter())
			using (var writer = XmlWriter.Create(stringWriter))
			{
				transform.Transform(RawDom.CreateNavigator(), writer);
				var result = new XmlDocument();
				result.LoadXml(stringWriter.ToString());
				return result;
			}
		}

		public string GetMetaValue(string name, string defaultValue)
		{
			var node = _dom.SafeSelectNodes("//head/meta[@name='" + name + "' or @name='" + name.ToLowerInvariant() + "']");
			if (node.Count > 0)
			{
				return ((XmlElement) node[0]).GetAttribute("content");
			}
			return defaultValue;
		}

		public void RemoveMetaElement(string name)
		{
			foreach (XmlElement n in _dom.SafeSelectNodes("//head/meta[@name='" + name + "']"))
			{
				n.ParentNode.RemoveChild(n);
			}
		}

		/// <summary>
		/// creates if necessary, then updates the named <meta></meta> in the head of the html
		/// </summary>
		public void UpdateMetaElement(string name, string value)
		{
			XmlElement n = _dom.SelectSingleNode("//meta[@name='" + name + "']") as XmlElement;
			if (n == null)
			{
				n = _dom.CreateElement("meta");
				n.SetAttribute("name", name);
				_dom.SelectSingleNode("//head").AppendChild(n);
			}
			n.SetAttribute("content", value);
		}

		/// <summary>
		/// Can be called without knowing that the old exists.
		/// If it already has the new, the old is just removed.
		/// This is just for migration.
		/// </summary>
		public void RemoveMetaElement(string oldName, Func<string> read, Action<string> write)
		{
			if (!HasMetaElement(oldName))
				return;

			if (!string.IsNullOrEmpty(read()))
			{
				RemoveMetaElement(oldName);
				return;
			}

			//ok, so we do have to transfer the value over

			write(GetMetaValue(oldName,""));

			//and remove any of the old name
			foreach(XmlElement node in _dom.SafeSelectNodes("//head/meta[@name='" + oldName + "']"))
			{
				node.ParentNode.RemoveChild(node);
			}

		}

		public bool HasMetaElement(string name)
		{
			return _dom.SafeSelectNodes("//head/meta[@name='" + name + "']").Count > 0;
		}

		public void RemoveExtraContentTypesMetas()
		{
			bool first=true;
			foreach (XmlElement n in _dom.SafeSelectNodes("//head/meta[@http-equiv='Content-Type']"))
			{
				if (first)//leave one
				{
					first = false;
					continue;
				}

				n.ParentNode.RemoveChild(n);
			}
		}

		public void AddStyleSheetIfMissing(string path)
		{
			// Remember, Linux filenames are case sensitive.
			var pathToCheck = path;
			if (Environment.OSVersion.Platform == PlatformID.Win32NT)
				pathToCheck = pathToCheck.ToLowerInvariant();
			foreach (XmlElement link in _dom.SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var fileName = link.GetStringAttribute("href");
				if (Environment.OSVersion.Platform == PlatformID.Win32NT)
					fileName = fileName.ToLowerInvariant();
				if (fileName == pathToCheck)
					return;
			}
			_dom.AddStyleSheet(path.Replace("file://", ""));
		}

		public IEnumerable<string> GetTemplateStyleSheets()
		{
			var stylesheetsToIgnore = new List<string>();
			// Remember, Linux filenames are case sensitive!
			stylesheetsToIgnore.Add("basePage.css");
			stylesheetsToIgnore.Add("languageDisplay.css");
			stylesheetsToIgnore.Add("editMode.css");
			stylesheetsToIgnore.Add("editOriginalMode.css");
			stylesheetsToIgnore.Add("previewMode.css");
			stylesheetsToIgnore.Add("settingsCollectionStyles.css");
			stylesheetsToIgnore.Add("customCollectionStyles.css");
			stylesheetsToIgnore.Add("customBookStyles.css");
			stylesheetsToIgnore.Add("XMatter");

			foreach (XmlElement link in _dom.SafeSelectNodes("//link[@rel='stylesheet']"))
			{
				var fileName = link.GetStringAttribute("href");
				var nameToCheck = fileName;
				if (Environment.OSVersion.Platform == PlatformID.Win32NT)
					nameToCheck = fileName.ToLowerInvariant();
				bool match = false;
				foreach (var nameOrFragment in stylesheetsToIgnore)
				{
					var nameStyle = nameOrFragment;
					if (Environment.OSVersion.Platform == PlatformID.Win32NT)
						nameStyle = nameStyle.ToLowerInvariant();
					if (nameToCheck.Contains(nameStyle))
					{
						match = true;
						break;
					}
				}
				if(!match)
					yield return fileName;
			}
		}


		public void AddPublishClassToBody()
		{
			AddPublishClassToBody(_dom);
		}


		/// <summary>
		/// By including this class, we help stylesheets do something different for edit vs. publish mode.
		/// </summary>
		public static void AddPublishClassToBody(XmlDocument dom)
		{
			AddClass((XmlElement)dom.SelectSingleNode("//body"),"publishMode");
		}

		public static void AddHidePlaceHoldersClassToBody(XmlDocument dom)
		{
			AddClass((XmlElement)dom.SelectSingleNode("//body"), "hidePlaceHolders");
		}

		/// <summary>
		/// The chosen xmatter changes, so we need to clear out any old ones
		/// </summary>
		public void RemoveXMatterStyleSheets()
		{
			foreach(XmlElement linkNode in RawDom.SafeSelectNodes("/html/head/link"))
			{
				var href = linkNode.GetAttribute("href");
				if (Path.GetFileName(href).ToLowerInvariant().EndsWith("xmatter.css"))
				{
					linkNode.ParentNode.RemoveChild(linkNode);
				}
			}
		}

		internal void RemoveStyleSheetIfFound(string path)
		{
			XmlDomExtensions.RemoveStyleSheetIfFound(RawDom, path);
		}

		/* The following, to use normal url query parameters to say if we wanted transparency,
		 * was a nice idea, but turned out to not be necessary. I'm leave the code here in
		 * case in the future we do find a need to add query parameters.
		public  void SetImagesForMode(bool editMode)
		{
			SetImagesForMode((XmlNode)RawDom, editMode);
		}

		public static void SetImagesForMode(XmlNode pageNode, bool editMode)
		{
			foreach(XmlElement imgNode in pageNode.SafeSelectNodes(".//img"))
			{
				var src = imgNode.GetAttribute("src");
				const string kTransparent = "?makeWhiteTransparent=true";
				src = src.Replace(kTransparent, "");
				if (editMode)
					src = src + kTransparent;
				imgNode.SetAttribute("src",src);
			}
		}
		*/

		public static void ProcessPageAfterEditing(XmlElement page, XmlElement divElement)
		{
			// strip out any elements that are part of bloom's UI; we don't want to save them in the document or show them in thumbnails etc.
			// Thanks to http://stackoverflow.com/questions/1390568/how-to-match-attributes-that-contain-a-certain-string for the xpath.
			// The idea is to match class attriutes which have class bloom-ui, but may have other classes. We don't want to match
			// classes where bloom-ui is a substring, though, if there should be any. So we wrap spaces around the class attribute
			// and then see whether it contains bloom-ui surrounded by spaces.
			// However, we need to do this in the edited page before copying to the storage page, since we are about to suck
			// info from the edited page into the dataDiv and we don't want the bloom-ui elements in there either!
			foreach (
				var node in divElement.SafeSelectNodes("//*[contains(concat(' ', @class, ' '), ' bloom-ui ')]").Cast<XmlNode>().ToArray())
				node.ParentNode.RemoveChild(node);

			page.InnerXml = divElement.InnerXml;

			//Enhance: maybe we should just copy over all attributes?
			page.SetAttribute("class", divElement.GetAttribute("class"));
			//The SIL LEAD SHRP templates rely on "lang" on some ancestor to trigger the correct rules in labels.css.
			//Those get set by putting data-metalanguage on Page, which then leads to a lang='xyz'. Let's save that
			//back to the html in keeping with our goal of having the page look right if you were to just open the
			//html file in Firefox.
			page.SetAttribute("lang", divElement.GetAttribute("lang"));

			// Upon save, make sure we are not in layout mode.  Otherwise we show the sliders.
			foreach(
				var node in
					page.SafeSelectNodes(".//*[contains(concat(' ', @class, ' '), ' origami-layout-mode ')]").Cast<XmlNode>().ToArray())
			{
				string currentValue = node.Attributes["class"].Value;
				node.Attributes["class"].Value = currentValue.Replace("origami-layout-mode", "");
			}
		}
	}
}
