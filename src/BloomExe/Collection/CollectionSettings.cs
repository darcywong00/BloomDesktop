﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Xml.Serialization;
using Amazon.CloudFront.Model;
using Bloom.Book;
using L10NSharp;
using Palaso.Reporting;
using Palaso.UI.WindowsForms.WritingSystems;
using Palaso.WritingSystems;
using Palaso.Extensions;

namespace Bloom.Collection
{

	public class NewCollectionSettings : CollectionSettings
	{
		public string PathToSettingsFile;
	}

	/// <summary>
	/// A library corresponds to a single folder (with subfolders) on the disk.
	/// In that folder is a file which persists the properties of this class, then a folder for each book
	/// </summary>
	public class CollectionSettings
	{
		private const int kCurrentOneTimeCheckVersionNumber = 1; // bumping this will trigger a new one time check

		public const string ReaderToolsSettingsPrefix = "ReaderToolsSettings-";
		private string _language1Iso639Code;
		private LookupIsoCodeModel _lookupIsoCode = new LookupIsoCodeModel();
		private Dictionary<string, string> _isoToLangNameDictionary = new Dictionary<string, string>();

		/// <summary>
		/// for moq in unit tests only
		/// </summary>
		public CollectionSettings()
		{
			XMatterPackName = "Factory";
			Language2Iso639Code = "en";
			AllowNewBooks = true;
		}

		public static void CreateNewCollection(NewCollectionSettings collectionInfo)
		{
			// For some reason this constructor is used to create new collections. But I think a static method is much clearer.
			new CollectionSettings(collectionInfo);
		}

		public CollectionSettings(NewCollectionSettings collectionInfo)
			:this(collectionInfo.PathToSettingsFile)
		{
			AllowNewBooks = collectionInfo.AllowNewBooks;
			DefaultLanguage1FontName = DefaultLanguage2FontName = DefaultLanguage3FontName = GetDefaultFontName();

			Language1Iso639Code = collectionInfo.Language1Iso639Code;
			Language2Iso639Code = collectionInfo.Language2Iso639Code;
			Language3Iso639Code = collectionInfo.Language3Iso639Code;
			Language1Name = collectionInfo.Language1Name;
			Country = collectionInfo.Country;
			Province = collectionInfo.Province;
			District = collectionInfo.District;
			IsSourceCollection = collectionInfo.IsSourceCollection;
			XMatterPackName = collectionInfo.XMatterPackName;
			Save();
		}

		/// <summary>
		/// can be used whether the library exists already, or not
		/// </summary>
		public CollectionSettings(string desiredOrExistingSettingsFilePath)
			:this()
		{
			SettingsFilePath = desiredOrExistingSettingsFilePath;
			CollectionName = Path.GetFileNameWithoutExtension(desiredOrExistingSettingsFilePath);
			var libraryDirectory = Path.GetDirectoryName(desiredOrExistingSettingsFilePath);
			var parentDirectoryPath = Path.GetDirectoryName(libraryDirectory);

			if (File.Exists(desiredOrExistingSettingsFilePath))
			{
				Load();
			}
			else
			{
				if (!Directory.Exists(parentDirectoryPath))
					Directory.CreateDirectory(parentDirectoryPath);

				if (!Directory.Exists(libraryDirectory))
					Directory.CreateDirectory(libraryDirectory);

				Save();
			}
		}

		#region Persisted properties

		//these are virtual for the sake of the unit test mock framework
		public virtual string Language1Iso639Code
		{
			get { return _language1Iso639Code; }
			set
			{
				_language1Iso639Code = value;
				Language1Name = GetLanguage1Name_NoCache(Language2Iso639Code);
			}
		}

		public virtual string Language2Iso639Code { get; set; }
		public virtual string Language3Iso639Code { get; set; }
		public virtual string Language1Name { get; set; }

		public virtual bool IsLanguage1Rtl { get; set; }
		public virtual bool IsLanguage2Rtl { get; set; }
		public virtual bool IsLanguage3Rtl { get; set; }

		public bool GetLanguageRtl(int langNum)
		{
			switch (langNum)
			{
				case 1:
					return IsLanguage1Rtl;
				case 2:
					return IsLanguage2Rtl;
				case 3:
					return IsLanguage3Rtl;
				default:
					throw new InvalidArgumentException("The language number is not valid.");
			}
		}

		public void SetLanguageRtl(int langNum, bool isRtl)
		{
			switch (langNum)
			{
				case 1:
					IsLanguage1Rtl = isRtl;
					break;
				case 2:
					IsLanguage2Rtl = isRtl;
					break;
				case 3:
					IsLanguage3Rtl = isRtl;
					break;
				default:
					throw new InvalidArgumentException("The language number is not valid.");
			}
		}

		public virtual decimal Language1LineHeight { get; set; }
		public virtual decimal Language2LineHeight { get; set; }
		public virtual decimal Language3LineHeight { get; set; }

		public decimal GetLanguageLineHeight(int langNum)
		{
			switch (langNum)
			{
				case 1:
					return Language1LineHeight;
				case 2:
					return Language2LineHeight;
				case 3:
					return Language3LineHeight;
				default:
					throw new InvalidArgumentException("The language number is not valid.");
			}
		}

		public void SetLanguageLineHeight(int langNum, decimal lineHeight)
		{
			switch (langNum)
			{
				case 1:
					Language1LineHeight = lineHeight;
					break;
				case 2:
					Language2LineHeight = lineHeight;
					break;
				case 3:
					Language3LineHeight = lineHeight;
					break;
				default:
					throw new InvalidArgumentException("The language number is not valid.");
			}
		}

		/// <summary>
		/// Intended for making shell books and templates, not vernacular
		/// </summary>
		public virtual bool IsSourceCollection { get; set; }

		public string GetLanguage1Name(string inLanguage)
		{
			if(!string.IsNullOrEmpty(this.Language1Name))
				return Language1Name;

			return GetLanguage1Name_NoCache(inLanguage);
		}

		private string GetLanguage1Name_NoCache(string inLanguage)
		{
			Iso639LanguageCode exactLanguageMatch = _lookupIsoCode.GetExactLanguageMatch(Language1Iso639Code);
			if (exactLanguageMatch == null)
				return "L1-Unknown-" + Language1Iso639Code;
			return GetLanguageNameInUILangIfPossible(exactLanguageMatch.Name, inLanguage);
		}

		public string GetLanguage2Name(string inLanguage)
		{
			try
			{
				//TODO: we are going to need to show "French" as "Français"... but if the name isn't available, we should have a fall-back mechanism, at least to english
				//So, we'd rather have GetBestLanguageMatch()

				return GetLanguageName(Language2Iso639Code, inLanguage);
			}
			catch (Exception)
			{
				Debug.Fail("check this out. BL-193 Reproduction");
				// a user reported this, and I saw it happen once: had just installed 0.8.38, made a new vernacular
				//project, added a picture dictionary, the above failed (no debugger, so I don't know why).
				return "L2-Unknown-" + Language2Iso639Code;
			}
		}

		/// <summary>
		/// Get the name of the language whose code is the first argument, if possible in the language specified by the second.
		/// If the language code is unknown, return it unchanged.
		/// </summary>
		/// <param name="code"></param>
		/// <param name="inLanguage"></param>
		/// <returns></returns>
		public string GetLanguageName(string code, string inLanguage)
		{
			//profiling showed we were spending a lot of time looking this up, hence the cache
			if (!_isoToLangNameDictionary.ContainsKey(code))
			{
				var match = _lookupIsoCode.GetExactLanguageMatch(code);
				if (match == null)
					_isoToLangNameDictionary[code] = code; // best name we can come up with is the code itself
				else
					_isoToLangNameDictionary.Add(code, match.Name);
			}

			return GetLanguageNameInUILangIfPossible(_isoToLangNameDictionary[code], inLanguage);
		}

		private string GetLanguageNameInUILangIfPossible(string name, string codeOfDesiredLanguage)
		{
			//we don't have a general way to get the language names translated yet. But at least we can show a few languages properly

			if (codeOfDesiredLanguage == "fr" && name == "French")
				return "français";
			if(codeOfDesiredLanguage == "th" && name == "Thai")
				return "ภาษา ไทย";
			if(codeOfDesiredLanguage == "ar" && name == "Arabic")
				return "العربية";
			if (codeOfDesiredLanguage == "es" && name == "Spanish")
				return "español";
			return name;
		}

		public string GetLanguage3Name(string inLanguage)
		{
			try
			{
				if (string.IsNullOrEmpty(Language3Iso639Code))
					return string.Empty;

				return GetLanguageName(Language3Iso639Code, inLanguage);
			}
			catch (Exception)
			{
				return "L2N-Unknown-" + Language3Iso639Code;
			}
		}
		#endregion

		/// ------------------------------------------------------------------------------------
		public void Save()
		{
			Logger.WriteEvent("Saving Collection Settings");

			XElement library = new XElement("Collection");
			library.Add(new XAttribute("version", "0.2"));
			library.Add(new XElement("Language1Name", Language1Name));
			library.Add(new XElement("Language1Iso639Code", Language1Iso639Code));
			library.Add(new XElement("Language2Iso639Code", Language2Iso639Code));
			library.Add(new XElement("Language3Iso639Code", Language3Iso639Code));
			library.Add(new XElement("DefaultLanguage1FontName", DefaultLanguage1FontName));
			library.Add(new XElement("DefaultLanguage2FontName", DefaultLanguage2FontName));
			library.Add(new XElement("DefaultLanguage3FontName", DefaultLanguage3FontName));
			library.Add(new XElement("OneTimeCheckVersionNumber", OneTimeCheckVersionNumber));
			library.Add(new XElement("IsLanguage1Rtl", IsLanguage1Rtl));
			library.Add(new XElement("IsLanguage2Rtl", IsLanguage2Rtl));
			library.Add(new XElement("IsLanguage3Rtl", IsLanguage3Rtl));
			library.Add(new XElement("Language1LineHeight", Language1LineHeight));
			library.Add(new XElement("Language2LineHeight", Language2LineHeight));
			library.Add(new XElement("Language3LineHeight", Language3LineHeight));
			library.Add(new XElement("IsSourceCollection", IsSourceCollection.ToString()));
			library.Add(new XElement("XMatterPack", XMatterPackName));
			library.Add(new XElement("Country", Country));
			library.Add(new XElement("Province", Province));
			library.Add(new XElement("District", District));
			library.Add(new XElement("AllowNewBooks", AllowNewBooks.ToString()));
			library.Save(SettingsFilePath);

			SavesettingsCollectionStylesCss();
		}

		private void SavesettingsCollectionStylesCss()
		{
			string path = FolderPath.CombineForPath("settingsCollectionStyles.css");

			try
			{
				var sb = new StringBuilder();
				sb.AppendLine("/* These styles are controlled by the Settings dialog box in Bloom. */");
				sb.AppendLine("/* They many be over-ridden by rules in customCollectionStyles.css or customBookStyles.css */");
				AddFontCssRule(sb, "BODY", GetDefaultFontName(), 0);
				AddFontCssRule(sb, "[lang='" + Language1Iso639Code + "']", DefaultLanguage1FontName, Language1LineHeight);
				AddFontCssRule(sb, "[lang='" + Language2Iso639Code + "']", DefaultLanguage2FontName, Language2LineHeight);
				if (!string.IsNullOrEmpty(Language3Iso639Code))
				{
					AddFontCssRule(sb, "[lang='" + Language3Iso639Code + "']", DefaultLanguage3FontName, Language3LineHeight);
				}
				File.WriteAllText(path, sb.ToString());
			}
			catch (Exception error)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "Bloom was unable to update this file: {0}",path);
			}
		}

		private void AddFontCssRule(StringBuilder sb, string selector, string fontName, decimal lineHeight)
		{
			sb.AppendLine();
			sb.AppendLine(selector);
			sb.AppendLine("{");
			sb.AppendLine(" font-family: '" + fontName + "';");

			if (lineHeight > 0)
				sb.AppendLine(" line-height: " + lineHeight + ";");

			sb.AppendLine("}");
		}

		/// ------------------------------------------------------------------------------------
		public void Load()
		{
			try
			{
				XElement library = XElement.Load(SettingsFilePath);
				Language1Iso639Code = GetValue(library, "Language1Iso639Code", /* old name */GetValue(library, "Language1Iso639Code", ""));
				Language2Iso639Code = GetValue(library, "Language2Iso639Code",  /* old name */GetValue(library, "National1Iso639Code", "en"));
				Language3Iso639Code = GetValue(library, "Language3Iso639Code",  /* old name */GetValue(library, "National2Iso639Code", ""));
				XMatterPackName = GetValue(library, "XMatterPack", "Factory");
				Language1Name = GetValue(library, "Language1Name",  /* old name */GetValue(library, "LanguageName", ""));
				DefaultLanguage1FontName = GetValue(library, "DefaultLanguage1FontName", GetDefaultFontName());
				DefaultLanguage2FontName = GetValue(library, "DefaultLanguage2FontName", GetDefaultFontName());
				DefaultLanguage3FontName = GetValue(library, "DefaultLanguage3FontName", GetDefaultFontName());
				OneTimeCheckVersionNumber = GetIntegerValue(library, "OneTimeCheckVersionNumber", 0);
				IsLanguage1Rtl = GetBoolValue(library, "IsLanguage1Rtl", false);
				IsLanguage2Rtl = GetBoolValue(library, "IsLanguage2Rtl", false);
				IsLanguage3Rtl = GetBoolValue(library, "IsLanguage3Rtl", false);
				Language1LineHeight = GetDecimalValue(library, "Language1LineHeight", 0);
				Language2LineHeight = GetDecimalValue(library, "Language2LineHeight", 0);
				Language3LineHeight = GetDecimalValue(library, "Language3LineHeight", 0);

				Country = GetValue(library, "Country", "");
				Province = GetValue(library, "Province", "");
				District = GetValue(library, "District", "");
				AllowNewBooks = GetBoolValue(library, "AllowNewBooks", true);
				IsSourceCollection = GetBoolValue(library, "IsSourceCollection", GetBoolValue(library, "IsShellLibrary" /*the old name*/, GetBoolValue(library, "IsShellMakingProject" /*an even older name*/, false)));
			}
			catch (Exception e)
			{
				ApplicationException a = new ApplicationException(File.ReadAllText(SettingsFilePath), e);
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(e,
																 "There was an error reading the library settings file.  Please report this error to the developers. To get access to your books, you should make a new library, then copy your book folders from this broken library into the new one, then run Bloom again.");
				throw;
			}

			try
			{
				string oldcustomCollectionStylesPath = FolderPath.CombineForPath("collection.css");
				if(File.Exists(oldcustomCollectionStylesPath))
				{
					string newcustomCollectionStylesPath = FolderPath.CombineForPath("customCollectionStyles.css");

					File.Move(oldcustomCollectionStylesPath, newcustomCollectionStylesPath);
				}
			}
			catch (Exception)
			{
				//ah well, we tried, no big deal, only a couple of beta testers used this old name
			}

			// Check if we need to do a one time check (perhaps migrate to a new Settings value)
			if (OneTimeCheckVersionNumber < kCurrentOneTimeCheckVersionNumber)
			{
				DoOneTimeCheck();
			}
		}

		private void DoOneTimeCheck()
		{
			// If we ever have to do another one of these besides our minimal Andika New Basic migration
			// we should refactor this so it calls a method based on the OneTimeCheckVersionNumber
			do
			{
				if(!MigrateSettingsToAndikaNewBasicFont())
					break; // in case of failed migration
				OneTimeCheckVersionNumber++;

			} while (OneTimeCheckVersionNumber < kCurrentOneTimeCheckVersionNumber);
			Save(); // save updated settings
		}

		private bool MigrateSettingsToAndikaNewBasicFont()
		{
			const string newFont = "Andika New Basic";
			if (GetDefaultFontName() != newFont) // sanity check to make sure Andika New Basic is installed
				return false;

			const string id = "CollectionSettingsDialog.AndikaNewBasicUpdate";
			var basicMessage = LocalizationManager.GetDynamicString("Bloom", id + "1",
				"Bloom is switching the default font for \"{0}\" to the new \"Andika New Basic\".");
			var secondaryMessage = LocalizationManager.GetDynamicString("Bloom", id + "2",
				"This will improve the printed output for most languages. If your language is one of the few that need \"Andika\", you can switch it back in Settings:Book Making.");
			const string oldFont = "Andika";
			var safeLanguages = new[] {"en", "es", "fr", "id", "tpi"};
			string msg = string.Empty;
			if(DefaultLanguage1FontName == oldFont)
			{
				DefaultLanguage1FontName = newFont;
				if (!safeLanguages.Contains(Language1Iso639Code))
				{
					msg += String.Format(basicMessage, Language1Name) + Environment.NewLine;
				}
			}
			if (DefaultLanguage2FontName == oldFont)
			{
				DefaultLanguage2FontName = newFont;
				if (!String.IsNullOrEmpty(Language2Iso639Code) && !safeLanguages.Contains(Language2Iso639Code))
				{
					msg += String.Format(basicMessage, GetLanguage2Name(Language2Iso639Code)) + Environment.NewLine;
				}
			}
			if (DefaultLanguage3FontName == oldFont)
			{
				DefaultLanguage3FontName = newFont;
				if (!String.IsNullOrEmpty(Language3Iso639Code) && !safeLanguages.Contains(Language3Iso639Code))
				{
					msg += String.Format(basicMessage, GetLanguage3Name(Language3Iso639Code)) + Environment.NewLine;
				}
			}
			// Only notify the user if the change involves a language that is not known to be okay with
			// the new font.
			if (!String.IsNullOrEmpty(msg) && ErrorReport.IsOkToInteractWithUser)
			{
				msg += Environment.NewLine + secondaryMessage;
				MessageBox.Show(msg, String.Empty, MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			return true;
		}

		private bool GetBoolValue(XElement library, string id, bool defaultValue)
		{
			string s = GetValue(library, id, defaultValue.ToString());
			bool b;
			bool.TryParse(s, out b);
			return b;
		}

		private int GetIntegerValue(XElement library, string id, int defaultValue)
		{
			var s = GetValue(library, id, defaultValue.ToString(CultureInfo.InvariantCulture));
			int i;
			int.TryParse(s, out i);
			return i;
		}

		private decimal GetDecimalValue(XElement library, string id, decimal defaultValue)
		{
			var s = GetValue(library, id, defaultValue.ToString(CultureInfo.InvariantCulture));
			decimal d;
			decimal.TryParse(s, out d);
			return d;
		}

		private string GetValue(XElement document, string id, string defaultValue)
		{
			var nodes = document.Descendants(id);
			if (nodes != null && nodes.Count() > 0)
				return nodes.First().Value;
			else
			{
				return defaultValue;
			}
		}


		public virtual string CollectionName { get; protected set; }

		/// <summary>
		/// The file (currently at a fixed location in every settings folder) where we store any settings
		/// related to Decodable and Leveled Readers.
		/// </summary>
		public string DecodableLevelPathName
		{
			get { return Path.Combine(Path.GetDirectoryName(SettingsFilePath), ReaderToolsSettingsPrefix + Language1Iso639Code + ".json"); }
		}

		[XmlIgnore]
		public string FolderPath
		{
			get { return Path.GetDirectoryName(SettingsFilePath); }
		}

		[XmlIgnore]
		public string SettingsFilePath { get; set; }

		/// <summary>
		/// for the "Factory-XMatter.htm", this would be named "Factory"
		/// </summary>
		virtual public string XMatterPackName { get; set; }

		virtual public string Country { get; set; }
		virtual public string Province { get; set; }
		virtual public string District { get; set; }

		public string VernacularCollectionNamePhrase
		{
			get
			{
				//review: in June 2013, I made it just use the collectionName regardless of the type. I wish I'd make a comment with the previous approach
				//explaining *why* we would wnat to just say, for example, "Foobar Books". Probably for some good reason.
				//But it left us with the weird situation of being able to chang the collection name in the settings, and have that only affect the  title
				//bar of the window (and the on-disk name). People wanted to change to a language name they want to see. (We'll probably have to do something
				//to enable that anyhow because it shows up elsewhere, but this is a step).
				//if(IsSourceCollection)
					return CollectionName;
				//var fmt = L10NSharp.LocalizationManager.GetString("CollectionTab.Vernacular Collection Heading", "{0} Books", "The {0} is where we fill in the name of the Vernacular");
				//return string.Format(fmt, Language1Name);
			}
		}

		public string DefaultLanguage1FontName { get; set; }

		public string DefaultLanguage2FontName { get; set; }

		public string DefaultLanguage3FontName { get; set; }

		public int OneTimeCheckVersionNumber { get; set; }

		public bool AllowNewBooks { get; set; }

		public bool AllowDeleteBooks
		{
			get { return AllowNewBooks; } //at the moment, we're combining these two concepts; we can split them if a good reason to comes along
		}


		public static string GetPathForNewSettings(string parentFolderPath, string newCollectionName)
		{
			return parentFolderPath.CombineForPath(newCollectionName, newCollectionName + ".bloomCollection");
		}


		public static string RenameCollection(string fromDirectory, string toDirectory)
		{
			if (!Directory.Exists(fromDirectory))
			{
				throw new ApplicationException("Bloom could not complete the renaming of the collection, because there isn't a directory with the source name anymore: " + fromDirectory);
			}

			if (Directory.Exists(toDirectory)) //there's already a folder taking this name
			{
				throw new ApplicationException("Bloom could not complete the renaming of the collection, because there is already a directory with the new name: " + toDirectory);
			}

			//this is just a sanity check, it will throw if the existing directory doesn't have a collection
			FindSettingsFileInFolder(fromDirectory);

//first rename the directory, as that is the part more likely to fail (because *any* locked file in there will cause a failure)
			Directory.Move(fromDirectory, toDirectory);
			string  collectionSettingsPath;
			try
			{
				collectionSettingsPath = FindSettingsFileInFolder(toDirectory);
			}
			catch (Exception)
			{
				throw;
			}

			try
			{
				//we now make a default name based on the name of the directory
				string destinationPath = Path.Combine(toDirectory, Path.GetFileName(toDirectory)+".bloomCollection");
				if (!File.Exists(destinationPath))
					File.Move(collectionSettingsPath, destinationPath);

				return destinationPath;
			}
			catch (Exception error)
			{
				//change the directory name back, so the rename isn't half-done.
				Directory.Move(toDirectory, fromDirectory);
				throw new ApplicationException(string.Format("Could change the folder name, but not the collection file name",fromDirectory,toDirectory),error);
			}
		}

		private string GetDefaultFontName()
		{
			foreach(var candidate in new[] { "Andika New Basic", "Andika", "Gentium", "Charis", "Paduak"/*Myanmar*/})
			{
				string lower = candidate.ToLowerInvariant();
				if (FontFamily.Families.FirstOrDefault(f =>
														   {
															   return f.Name.ToLowerInvariant() == lower;
														   }) != null)
				{
					return candidate;
				}
			}
			return SystemFonts.DefaultFont.Name;
		}

		public static string FindSettingsFileInFolder(string folderPath)
		{
			try
			{
				return Directory.GetFiles(folderPath, "*.bloomCollection").First();
			}
			catch (Exception)
			{
				throw new ApplicationException(string.Format("Bloom expected to find a .bloomCollectionFile in {0}, but there isn't one.", folderPath));
			}
		}

		internal LanguageDescriptor[] MakeLanguageUploadData(string[] isoCodes)
		{
			var result = new LanguageDescriptor[isoCodes.Length];
			for (int i = 0; i < isoCodes.Length; i++)
			{
				var code = isoCodes[i];
				var data = _lookupIsoCode.GetExactLanguageMatch(code);
				string name;
				if (code == Language1Iso639Code)
					name = Language1Name;
				else if (data == null)
					name = code;
				else
					name = data.Name;
				string ethCode;
				if (data == null)
					ethCode = code;
				else
				{
					ethCode = data.ISO3Code;
					if (string.IsNullOrEmpty(ethCode))
						ethCode = code;
				}
				result[i] = new LanguageDescriptor() { IsoCode = code, Name = name, EthnologueCode = ethCode };
			}
			return result;
		}

		/// <summary>
		/// Given a choice, what language should we use to describe the license on the page (not in the UI, which is controlled by the UI Language)
		/// </summary>
		public IEnumerable<string> LicenseDescriptionLanguagePriorities
		{
			get { return new[] { Language1Iso639Code, Language2Iso639Code, Language3Iso639Code, "en" }; }
		}
	}
}
