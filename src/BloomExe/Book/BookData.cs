﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms.VisualStyles;
using System.Xml;
using System.Xml.Linq;
using Bloom.Collection;
using L10NSharp;
using Palaso.Code;
using Palaso.Text;
using Palaso.UI.WindowsForms.ClearShare;
using Palaso.Xml;
using RestSharp;

namespace Bloom.Book
{
	/// <summary>
	/// This class manages the "data-*" elements of a bloom document.
	/// </summary>
	/// <remarks>
	/// At the beginning of the document, we have a special div for holding book-wide data.
	/// It may hosts all maner of data about the book, including copyright, what languages are currently visible, etc.Here's a sample of a simple one:
	/*<div id="bloomDataDiv">
			  <div data-book="bookTitle" lang="en">Awito Builds a toilet</div>
			  <div data-book="bookTitle" lang="tpi">Awito i wokim haus</div>
			  <div data-book="coverImage" lang="*">tmpABDB.png</div>
			  <div data-book="topic" lang="tpi">Health</div>
			  <div data-book="contentLanguage1" lang="*">en</div>
			  <div data-book="contentLanguage2" lang="*">tpi</div>
			  <div data-book="copyright" lang="*">Copyright © 1994, National Department of Education</div>
			  <div data-book="licenseImage" lang="*">license.png?1348557455942</div>
			  <div data-book="licenseUrl" lang="en">http://creativecommons.org/licenses/by-nc-sa/3.0/</div>
			  <div data-book="licenseDescription" lang="en">You may not use this work for commercial purposes. You may adapt or build upon this work, but you may distribute the resulting work only under the same or similar license to this one.You must attribute the work in the manner specified by the author.</div>
			  <div data-book="originalAcknowledgments" lang="tpi">Book Development by:  Curriculum Development Division</div>
			</div>
			*/
	/// After the bloomDataDiv, elements with "data-*" attributes can occur throughout a book, for example on the cover page:
	/*    <div class="bloom-page">
		<div class="bloom-translationGroup coverTitle">
		  <div data-book="bookTitle" lang="en">Awito Builds a house</div>
		  <div data-book="bookTitle" lang="tpi">Awito i wokim haus</div>
		</div>
	*/
	/// This class must keep these in sync
	/// There is also a file meta.json which contains data that is also kept online to aid in searching for books. Some of this must also be kept
	/// in sync with data in the html, for example, metadata.volumeInfo.title should match (currently the English alternative of) the content of the
	/// bloomDataDiv bookTitle div.
	/// </remarks>
	public class BookData
	{
		private readonly HtmlDom _dom;
		private readonly Action<XmlElement> _updateImgNode;
		private readonly CollectionSettings _collectionSettings;
		private readonly DataSet _dataset;
		private XmlElement _dataDiv;
		private Object thisLock = new Object();

		/// <param name="dom">Set this parameter to, say, a page that the user just edited, to limit reading to it, so its values don't get overriden by previous pages.
		///   Supply the whole dom if nothing has priority (which will mean the data-div will win, because it is first)</param>
		/// <param name="collectionSettings"> </param>
		/// <param name="updateImgNodeCallback">This is a callback so as not to introduce dependencies on ImageUpdater & the current folder path</param>
		public BookData(HtmlDom dom, CollectionSettings collectionSettings, Action<XmlElement> updateImgNodeCallback)
		{
			_dom = dom;
			_updateImgNode = updateImgNodeCallback;
			_collectionSettings = collectionSettings;
			GetOrCreateDataDiv();
			_dataset = GatherDataItemsFromCollectionSettings(_collectionSettings);
			GatherDataItemsFromXElement(_dataset,_dom.RawDom);
			MigrateData();
		}

		/// <summary>
		/// For bilingual or trilingual books, this is the second language to show, after the vernacular
		/// </summary>
		public string MultilingualContentLanguage2
		{
			get
			{
				GatherDataItemsFromXElement(_dataset,_dom.RawDom);
				return GetVariableOrNull("contentLanguage2", "*");
			}
		}

		/// <summary>
		/// For trilingual books, this is the third language to show
		/// </summary>
		public string MultilingualContentLanguage3
		{
			get
			{
				GatherDataItemsFromXElement(_dataset, _dom.RawDom);
				return GetVariableOrNull("contentLanguage3", "*");
			}
		}

		/// <summary>
		/// A book-level style number sequence
		/// </summary>
		public int StyleNumberSequence
		{
			get
			{
				lock(thisLock)
				{
					GatherDataItemsFromXElement(_dataset, _dom.RawDom);
					string curSeqStr = GetVariableOrNull("styleNumberSequence", "*");
					int curSeq;
					int nextSeq = 1;
					if (Int32.TryParse(curSeqStr, out curSeq))
						nextSeq = curSeq + 1;
					Set("styleNumberSequence", nextSeq.ToString(CultureInfo.InvariantCulture),
						false);
					return nextSeq;
				}
			}
		}


		public void UpdateVariablesAndDataDivThroughDOM(BookInfo info = null)
		{
			UpdateVariablesAndDataDiv(_dom.RawDom.FirstChild, info);
		}


		/// <summary>
		/// Create or update the data div with all the data-book values in the document
		/// </summary>
		/// <param name="dom">This is either the whole document, or a page div that we just edited and want to read from.</param>
		public void SuckInDataFromEditedDom(HtmlDom dom)
		{
			UpdateVariablesAndDataDiv(dom.RawDom);
		}

		public void SynchronizeDataItemsThroughoutDOM()
		{
			var itemsToDelete = new HashSet<Tuple<string, string>>();
			SynchronizeDataItemsFromContentsOfElement(_dom.Body, itemsToDelete);
		}

		/// <summary>
		/// Create or update the data div with all the data-book values in the document
		/// </summary>
		/// <param name="elementToReadFrom">This is either the whole document, or a page div that we just edited and want to read from.</param>
		private void UpdateVariablesAndDataDiv(XmlNode elementToReadFrom, BookInfo info = null)
		{
			Debug.WriteLine("before update: " + _dataDiv.OuterXml);

			var itemsToDelete = new HashSet<Tuple<string, string>>();
			DataSet incomingData = SynchronizeDataItemsFromContentsOfElement(elementToReadFrom, itemsToDelete);
			incomingData.UpdateGenericLanguageString("contentLanguage1", _collectionSettings.Language1Iso639Code, false);
			incomingData.UpdateGenericLanguageString("contentLanguage2",
											 String.IsNullOrEmpty(MultilingualContentLanguage2)
												 ? null
												 : MultilingualContentLanguage2, false);
			incomingData.UpdateGenericLanguageString("contentLanguage3",
											 String.IsNullOrEmpty(MultilingualContentLanguage3)
												 ? null
												 : MultilingualContentLanguage3, false);

			//Debug.WriteLine("xyz: " + _dataDiv.OuterXml);
			foreach (var v in incomingData.TextVariables)
			{
				if (!v.Value.IsCollectionValue)
					UpdateSingleTextVariableInDataDiv(v.Key,v.Value.TextAlternatives);
			}
			foreach (var tuple in itemsToDelete)
				UpdateSingleTextVariableInDataDiv(tuple.Item1, tuple.Item2, "");
			Debug.WriteLine("after update: " + _dataDiv.OuterXml);

			UpdateTitle(info);//this may change our "bookTitle" variable if the title is based on a template that reads other variables (e.g. "Primer Term2-Week3")
			UpdateIsbn(info);
			UpdateTags(info);
			UpdateCredits(info);
		}

		private void MigrateData()
		{
			//Until late in Bloom 3, we collected the topic in the National language, which is messy because then we would have to know how to 
			//translate from all those languages to all other languages. Now, we just save English, and translate from English to whatever.
			//By far the largest number of books posted to bloomlibrary with this problem were Tok Pisin books, which actually just had
			//an English word as their value for "topic", so there we just switch it over to English.
			NamedMutliLingualValue topic;
			if(_dataset.TextVariables.TryGetValue("topic", out topic))
			{
				var topicStrings = topic.TextAlternatives;
				if (string.IsNullOrEmpty(topicStrings["en"] ) && topicStrings["tpi"] != null)
				{
					topicStrings["en"] = topicStrings["tpi"];

					topicStrings.RemoveLanguageForm(topicStrings.Find("tpi"));
				}
			}
		}

		private void UpdateCredits(BookInfo info)
		{
			if (info == null)
				return;

			NamedMutliLingualValue creditsData;
			string credits = "";
			if (_dataset.TextVariables.TryGetValue("originalAcknowledgments", out creditsData))
			{
				credits = creditsData.TextAlternatives.GetBestAlternativeString(WritingSystemIdsToTry);
			}
			info.Credits = credits.Replace("<br />", ""); // Clean out breaks inserted at newlines.
		}

		/// <summary>
		/// grabs the english (which serves as the 'key') from the datadiv and then adds or updates
		/// the equivalent for the current cover language
		/// </summary>
		/// <param name="data"></param>
		private void UpdateTopicInLanguageOfCover(DataSet data)
		{
			NamedMutliLingualValue topicData;
			if(data.TextVariables.TryGetValue("topic", out topicData))
			{
				//we use English as the "key" for topics.
				var englishTopic = topicData.TextAlternatives.GetExactAlternative("en");
				if (string.IsNullOrEmpty(englishTopic))
					return;
				string langOfTopicToShowOnCover = _collectionSettings.Language2Iso639Code;
				var id = "Topics." + englishTopic;

				string s = "";
				
				var bestTranslation = LocalizationManager.GetDynamicStringOrEnglish("Bloom", id, englishTopic, "this is a book topic", langOfTopicToShowOnCover);;
				//NB: in a unit test environment, GetDynamicStringOrEnglish is going to give us the id back, which is annoying.
				if (bestTranslation == id)
					bestTranslation = englishTopic;
				data.AddLanguageString("topic", bestTranslation, langOfTopicToShowOnCover, false);
			}
		}

		private void UpdateIsbn(BookInfo info)
		{
			if (info == null)
				return;

			NamedMutliLingualValue isbnData;
			string isbn = null;
			if (_dataset.TextVariables.TryGetValue("ISBN", out isbnData))
			{
				isbn = isbnData.TextAlternatives.GetBestAlternativeString(WritingSystemIdsToTry); // Review: not really multilingual data, do we need this?
			}
			info.Isbn = isbn ?? "";
		}

		// For now, when there is no UI for multiple tags, we make Tags a single item, the book topic.
		// It's not clear what we will want to do when the topic changes and there is a UI for (possibly multiple) tags.
		// Very likely we still want to add the new topic (if it is not already present).
		// Should we still remove the old one?
		private void UpdateTags(BookInfo info)
		{
			if (info == null)
				return;

			NamedMutliLingualValue tagData;
			string tag = null;
			if (_dataset.TextVariables.TryGetValue("topic", out tagData))
			{
				tag = tagData.TextAlternatives.GetBestAlternativeString(WritingSystemIdsToTry);
			}

			if (tag == null)
				return;

			// In case we're running localized, for now we'd like to record in the metadata the original English tag.
			// This allows the book to be found by this tag in the current, non-localized version of bloom library.
			// Eventually it will make it easier, we think, to implement localization of bloom library.
			string originalTag;
			if (RuntimeInformationInjector.TopicReversal == null ||
				!RuntimeInformationInjector.TopicReversal.TryGetValue(tag, out originalTag))
			{
				originalTag = tag; // just use it unmodified if we don't have anything
			}
			info.TagsList = originalTag;
		}


		/// <summary>
		///
		/// </summary>
		/// <remarks>I (jh) found this labelled UpdateSingleTextVariableThrougoutDom but it actually only updated the datadiv, so I changed the name.</remarks>
		/// <param name="key"></param>
		/// <param name="multiText"></param>
		private void UpdateSingleTextVariableInDataDiv(string key, MultiTextBase multiText)
		{
			//Debug.WriteLine("before: " + dataDiv.OuterXml);

			if(multiText.Count==0)
			{
				RemoveDataDivElementIfEmptyValue(key,null);
			}
			foreach (LanguageForm languageForm in multiText.Forms)
			{
				string writingSystemId = languageForm.WritingSystemId;
				UpdateSingleTextVariableInDataDiv(key, writingSystemId, languageForm.Form);
			}
		}

		/// <summary>
		///
		/// </summary>
		/// <remarks>I (jh) found this labelled UpdateSingleTextVariableThrougoutDom but it actually only updated the datadiv, so I changed the name.</remarks>
		/// <param name="key"></param>
		/// <param name="writingSystemId"></param>
		/// <param name="form"></param>
		private void UpdateSingleTextVariableInDataDiv(string key, string writingSystemId, string form)
		{
			XmlNode node =
				_dataDiv.SelectSingleNode(String.Format("div[@data-book='{0}' and @lang='{1}']", key,
					writingSystemId));

			_dataset.UpdateLanguageString(key, form, writingSystemId, false);

			if (null == node)
			{
				if (!string.IsNullOrEmpty(form))
				{
					Debug.WriteLine("creating in datadiv: {0}[{1}]={2}", key, writingSystemId, form);
					Debug.WriteLine("nop: " + _dataDiv.OuterXml);
					AddDataDivBookVariable(key, writingSystemId, form);
				}
			}
			else
			{
				if (string.IsNullOrEmpty(form)) //a null value removes the entry entirely
				{
					node.ParentNode.RemoveChild(node);
				}
				else
				{
					node.InnerXml = form;
				}
				//Debug.WriteLine("updating in datadiv: {0}[{1}]={2}", key, languageForm.WritingSystemId,
				//				languageForm.Form);
				//Debug.WriteLine("now: " + _dataDiv.OuterXml);
			}
		}

		public void AddDataDivBookVariable(string key, string lang, string form)
		{
			XmlElement d = _dom.RawDom.CreateElement("div");
			d.SetAttribute("data-book", key);
			d.SetAttribute("lang", lang);
			d.InnerXml = form;
			GetOrCreateDataDiv().AppendChild(d);
		}

		public void Set(string key, string value, bool isCollectionValue)
		{
			_dataset.UpdateGenericLanguageString(key, value, isCollectionValue);
			UpdateSingleTextVariableInDataDiv(key, _dataset.TextVariables[key].TextAlternatives);
		}

		public void Set(string key, string value, string lang)
		{
			_dataset.UpdateLanguageString(key, value, lang, false);
			if(_dataset.TextVariables.ContainsKey(key))
			{
				UpdateSingleTextVariableInDataDiv(key,_dataset.TextVariables[key].TextAlternatives);
			}
			else //we go this path if we just removed the last value from the multitext
			{
				RemoveDataDivElementIfEmptyValue(key, value);
			}
		}

		public void RemoveSingleForm(string key, string lang)
		{
			Set(key, null, lang);
		}
		public void RemoveAllForms(string key)
		{
			XmlElement dataDiv = GetOrCreateDataDiv();
			foreach (XmlNode e in dataDiv.SafeSelectNodes(String.Format("div[@data-book='{0}']", key)))
			{
				dataDiv.RemoveChild(e);
			}
			if (_dataset.TextVariables.ContainsKey(key))
			{
				_dataset.TextVariables.Remove(key);
			}
		}


		private XmlElement GetOrCreateDataDiv()
		{
			if(_dataDiv!=null)
				return _dataDiv;
			_dataDiv = _dom.RawDom.SelectSingleNode("//div[@id='bloomDataDiv']") as XmlElement;
			if (_dataDiv == null)
			{
				_dataDiv = _dom.RawDom.CreateElement("div");
				_dataDiv.SetAttribute("id", "bloomDataDiv");
				_dom.RawDom.SelectSingleNode("//body").InsertAfter(_dataDiv, null);
			}
			return _dataDiv;
		}



		/// <summary>
		/// Go through the document, reading in values from fields, and then pushing variable values back into fields.
		/// Here we're calling "fields" the html supplying or receiving the data, and "variables" being key-value pairs themselves, which
		/// are, for library variables, saved in a separate file.
		/// </summary>
		/// <param name="elementToReadFrom"> </param>
		private DataSet SynchronizeDataItemsFromContentsOfElement(XmlNode elementToReadFrom, HashSet<Tuple<string, string>> itemsToDelete)
		{
			DataSet data = GatherDataItemsFromCollectionSettings(_collectionSettings);

			// The first encountered value for data-book/data-collection wins... so the rest better be read-only to the user, or they're in for some frustration!
			// If we don't like that, we'd need to create an event to notice when field are changed.

			GatherDataItemsFromXElement(data, elementToReadFrom, itemsToDelete);
//            SendDataToDebugConsole(data);
			UpdateDomFromDataSet(data, "*", _dom.RawDom, itemsToDelete);

			UpdateTitle();

			return data;
		}

		private static DataSet GatherDataItemsFromCollectionSettings(CollectionSettings collectionSettings)
		{
			var data = new DataSet();

			data.WritingSystemAliases.Add("N1", collectionSettings.Language2Iso639Code);
			data.WritingSystemAliases.Add("N2", collectionSettings.Language3Iso639Code);

//            if (makeGeneric)
//            {
//                data.WritingSystemCodes.Add("V", collectionSettings.Language2Iso639Code);
//                    //This is not an error; we don't want to use the verncular when we're just previewing a book in a non-verncaulr collection
//                data.AddGenericLanguageString("iso639Code", collectionSettings.Language1Iso639Code, true);
//                    //review: maybe this should be, like 'xyz"
//                data.AddGenericLanguageString("nameOfLanguage", "(Your Language Name)", true);
//                data.AddGenericLanguageString("nameOfNationalLanguage1", "(Region Lang)", true);
//                data.AddGenericLanguageString("nameOfNationalLanguage2", "(National Lang)", true);
//                data.AddGenericLanguageString("country", "Your Country", true);
//                data.AddGenericLanguageString("province", "Your Province", true);
//                data.AddGenericLanguageString("district", "Your District", true);
//                data.AddGenericLanguageString("languageLocation", "(Language Location)", true);
//            }
//            else
			{
				data.WritingSystemAliases.Add("V", collectionSettings.Language1Iso639Code);
				data.AddLanguageString("nameOfLanguage", collectionSettings.Language1Name, "*", true);
				data.AddLanguageString("nameOfNationalLanguage1",
									   collectionSettings.GetLanguage2Name(collectionSettings.Language2Iso639Code), "*", true);
				data.AddLanguageString("nameOfNationalLanguage2",
									   collectionSettings.GetLanguage3Name(collectionSettings.Language2Iso639Code), "*", true);
				data.UpdateGenericLanguageString("iso639Code", collectionSettings.Language1Iso639Code, true);
				data.UpdateGenericLanguageString("country", collectionSettings.Country, true);
				data.UpdateGenericLanguageString("province", collectionSettings.Province, true);
				data.UpdateGenericLanguageString("district", collectionSettings.District, true);
				string location = "";
				if (!String.IsNullOrEmpty(collectionSettings.District))
					location += collectionSettings.District + @", ";
				if (!String.IsNullOrEmpty(collectionSettings.Province))
					location += collectionSettings.Province + @", ";

				if (!String.IsNullOrEmpty(collectionSettings.Country))
				{
					location += collectionSettings.Country;
				}

				location = location.TrimEnd(new[] { ' ' }).TrimEnd(new[] { ',' });

				data.UpdateGenericLanguageString("languageLocation", location, true);
			}
			return data;
		}

		/// <summary>
		/// Give the string the user expects to see as the name of a specified language.
		/// This routine uses the user-specified name for the main project language.
		/// For the other two project languages, it explicitly uses the appropriate collection settings
		/// name for that language, though currently this gives the same result as the final default.
		/// This will find a fairly readable name for the languages Palaso knows about
		/// and fall back to the code itself if it can't find a name.
		/// Most names are not yet localized.
		/// </summary>
		/// <param name="code"></param>
		/// <returns></returns>
		public string PrettyPrintLanguage(string code)
		{
			if (code == _collectionSettings.Language1Iso639Code && !string.IsNullOrWhiteSpace(_collectionSettings.Language1Name))
				return _collectionSettings.Language1Name;
			if (code == _collectionSettings.Language2Iso639Code)
				return _collectionSettings.GetLanguage2Name(_collectionSettings.Language2Iso639Code);
			if (code == _collectionSettings.Language3Iso639Code)
				return _collectionSettings.GetLanguage3Name(_collectionSettings.Language2Iso639Code);
			return _collectionSettings.GetLanguageName(code, _collectionSettings.Language2Iso639Code);
		}

		/// <summary>
		/// walk throught the sourceDom, collecting up values from elements that have data-book or data-collection attributes.
		/// </summary>
		private void GatherDataItemsFromXElement(DataSet data,
			XmlNode sourceElement, // can be the whole sourceDom or just a page
			HashSet<Tuple<string, string>> itemsToDelete = null) // records key, lang pairs for which we found an empty element in the source.
		{
			string elementName = "*";
			try
			{
				string query = String.Format(".//{0}[(@data-book or @data-library or @data-collection) and not(contains(@class,'bloom-writeOnly'))]", elementName);

				XmlNodeList nodesOfInterest = sourceElement.SafeSelectNodes(query);

				foreach (XmlElement node in nodesOfInterest)
				{
					bool isCollectionValue = false;

					string key = node.GetAttribute("data-book").Trim();
					if (key == String.Empty)
					{
						key = node.GetAttribute("data-collection").Trim();
						if (key == String.Empty)
						{
							key = node.GetAttribute("data-library").Trim(); //the old (pre-version 1) name of collections was 'library'
						}
						isCollectionValue = true;
					}

					string value = node.InnerXml.Trim(); //may contain formatting
					if (node.Name.ToLowerInvariant() == "img")
					{
						value = node.GetAttribute("src");
						//Make the name of the image safe for showing up in raw html (not just in the relatively safe confines of the src attribut),
						//becuase it's going to show up between <div> tags.  E.g. "Land & Water.png" as the cover page used to kill us.
						value = WebUtility.HtmlEncode(WebUtility.HtmlDecode(value));
					}
					string lang = node.GetOptionalStringAttribute("lang", "*");
					if (lang == "") //the above doesn't stop a "" from getting through
						lang = "*";
					if (lang == "{V}")
						lang = _collectionSettings.Language1Iso639Code;
					if(lang == "{N1}")
						lang = _collectionSettings.Language2Iso639Code;
					if(lang == "{N2}")
						lang = _collectionSettings.Language3Iso639Code;

					if (string.IsNullOrEmpty(value))
					{
						// This is a value we may want to delete
						if (itemsToDelete != null)
							itemsToDelete.Add(Tuple.Create(key, lang));
					}
					else if (!value.StartsWith("{"))
						//ignore placeholder stuff like "{Book Title}"; that's not a value we want to collect
					{
						if ((elementName.ToLowerInvariant() == "textarea" || elementName.ToLowerInvariant() == "input" ||
							 node.GetOptionalStringAttribute("contenteditable", "false") == "true") &&
							(lang == "V" || lang == "N1" || lang == "N2"))
						{
							throw new ApplicationException(
								"Editable element (e.g. TextArea) should not have placeholder @lang attributes (V,N1,N2)\r\n\r\n" +
								node.OuterXml);
						}

						//if we don't have a value for this variable and this language, add it
						if (!data.TextVariables.ContainsKey(key))
						{
							var t = new MultiTextBase();
							t.SetAlternative(lang, value);
							data.TextVariables.Add(key, new NamedMutliLingualValue(t, isCollectionValue));
						}
						else if (!data.TextVariables[key].TextAlternatives.ContainsAlternative(lang))
						{
							MultiTextBase t = data.TextVariables[key].TextAlternatives;
							t.SetAlternative(lang, value);
						}
					}
				}
			}
			catch (Exception error)
			{
				throw new ApplicationException(
					"Error in GatherDataItemsFromDom(," + elementName + "). RawDom was:\r\n" + sourceElement.OuterXml,
					error);
			}
		}

		/// <summary>
		/// given the values in our dataset, push them out to the fields in the pages
		/// </summary>
		public void UpdateDomFromDataset()
		{
			var noItemsToDelete = new HashSet<Tuple<string, string>>();
			UpdateDomFromDataSet(_dataset, "*", _dom.RawDom, noItemsToDelete);
		}

		/// <summary>
		/// Where, for example, somewhere on a page something has data-book='foo' lan='fr',
		/// we set the value of that element to French subvalue of the data item 'foo', if we have one.
		/// </summary>
		private void UpdateDomFromDataSet(DataSet data, string elementName,XmlDocument targetDom, HashSet<Tuple<string, string>> itemsToDelete)
		{
			UpdateTopicInLanguageOfCover(data); //reveiw
			try
			{
				string query = String.Format("//{0}[(@data-book or @data-collection or @data-library)]", elementName);
				XmlNodeList nodesOfInterest = targetDom.SafeSelectNodes(query);

				foreach (XmlElement node in nodesOfInterest)
				{
					string key = node.GetAttribute("data-book").Trim();
					if (key == String.Empty)
					{
						key = node.GetAttribute("data-collection").Trim();
						if (key == string.Empty)
						{
							key = node.GetAttribute("data-library").Trim();
							//"library" is the old name for what is now "collection"
						}
					}

					if (!String.IsNullOrEmpty(key))
					{
						if (data.TextVariables.ContainsKey(key))
						{
							if (node.Name.ToLowerInvariant() == "img")
							{
								string imageName =
									WebUtility.HtmlDecode(data.TextVariables[key].TextAlternatives.GetFirstAlternative());
								string oldImageName = WebUtility.HtmlDecode(node.GetAttribute("src"));
								node.SetAttribute("src", imageName);
								if (oldImageName != imageName)
								{
									Guard.AgainstNull(_updateImgNode, "_updateImgNode");
									_updateImgNode(node);
								}
							}
							else
							{
								string lang = node.GetOptionalStringAttribute("lang", "*");
								if (lang == "N1" || lang == "N2" || lang == "V")
									lang = data.WritingSystemAliases[lang];

								//							//see comment later about the inability to clear a value. TODO: when we re-write Bloom, make sure this is possible
								//							if(data.TextVariables[key].TextAlternatives.Forms.Length==0)
								//							{
								//								//no text forms == desire to remove it. THe multitextbase prohibits empty strings, so this is the best we can do: completly remove the item.
								//								targetDom.RemoveChild(node);
								//							}
								//							else
								if (!String.IsNullOrEmpty(lang)) //if we don't even have this language specified (e.g. no national language), the  give up
								{
									//Ideally, we have this string, in this desired language.
									string s = data.TextVariables[key].TextAlternatives.GetBestAlternativeString(new[] {lang, "*"});

									//But if not, maybe we should copy one in from another national language
									if (string.IsNullOrEmpty(s))
										s = PossiblyCopyFromAnotherLanguage(node, lang, data, key);

									//NB: this was the focus of a multi-hour bug search, and it's not clear that I got it right.
									//The problem is that the title page has N1 and n2 alternatives for title, the cover may not.
									//the gather page was gathering no values for those alternatives (why not), and so GetBestAlternativeSTring
									//was giving "", which we then used to remove our nice values.
									//REVIEW: what affect will this have in other pages, other circumstances. Will it make it impossible to clear a value?
									//Hoping not, as we are differentiating between "" and just not being in the multitext at all.
									//don't overwrite a datadiv alternative with empty just becuase this page has no value for it.
									if (s == "" && !data.TextVariables[key].TextAlternatives.ContainsAlternative(lang))
										continue;

									//hack: until I think of a more elegant way to avoid repeating the language name in N2 when it's the exact same as N1...
									if (data.WritingSystemAliases.Count != 0 && lang == data.WritingSystemAliases["N2"] &&
										s ==
										data.TextVariables[key].TextAlternatives.GetBestAlternativeString(new[]
										{
											data.
												WritingSystemAliases
												["N1"]
											, "*"
										}))
									{
										s = ""; //don't show it in N2, since it's the same as N1
									}
									node.InnerXml = s;
									//meaning, we'll take "*" if you have it but not the exact choice. * is used for languageName, at least in dec 2011
								}
							}
						}
						else if (node.Name.ToLowerInvariant() != "img")
						{
							// See whether we need to delete something
							string lang = node.GetOptionalStringAttribute("lang", "*");
							if (lang == "N1" || lang == "N2" || lang == "V")
								lang = data.WritingSystemAliases[lang];
							if (itemsToDelete.Contains(Tuple.Create(key, lang)))
							{
								node.InnerXml = ""; // a later process may remove node altogether.
							}
						}
					}
				}
			}
			catch (Exception error)
			{
				throw new ApplicationException(
					"Error in UpdateDomFromDataSet(," + elementName + "). RawDom was:\r\n" +
					targetDom.OuterXml, error);
			}
		}

		/// <summary>
		/// In some cases, we're better off copying from another national language than leaving the field empty.
		/// </summary>
		/// <remarks>
		///	This is a tough decision. Without this, if we have, say, an English Contributors list but English isn't the N1 (L2), then the
		/// book won't show it at all. An ideal solution would just order them and then "display the first non-empty one", but that would require some java script... not
		/// something could be readily done in CSS, far as I can think.
		/// For now, I *think* this won't do any harm, and if it does, it's adding data, not losing it. Users had complained about "losing" the contributor data before.
		///</remarks>
		private string PossiblyCopyFromAnotherLanguage(XmlElement element, string languageCodeOfTargetField, DataSet data, string key)
		{
			string classes = element.GetAttribute("class");
			if (!string.IsNullOrEmpty(classes))
			{
				// if this field is normally read-only, make it readable so they can do any translation that might be needed
				element.SetAttribute("class", classes.Replace("bloom-readOnlyInTranslationMode", ""));
			}

			if (!classes.Contains("bloom-copyFromOtherLanguageIfNecessary"))
			{
				return "";
			}

			LanguageForm formToCopyFromSinceOursIsMissing = null;
			string s = "";

			if ((languageCodeOfTargetField == _collectionSettings.Language2Iso639Code || //is it a national language?
				 languageCodeOfTargetField == _collectionSettings.Language3Iso639Code) ||
				//this one is a kludge as we clearly have this case of a vernacular field that people have used
				//to hold stuff that should be copied to every shell. So we can either remove the restriction of the
				//first two clauses in this if statement, or add another bloom-___ class in order to make execptions.
				//Today, I'm not seing the issue clearly enough, so I'm just going to path this one exisint hole.
				 classes.Contains("smallCoverCredits"))
			{
				formToCopyFromSinceOursIsMissing =
					data.TextVariables[key].TextAlternatives.GetBestAlternative(new[] {languageCodeOfTargetField, "*", "en", "fr", "es", "pt"});
				if (formToCopyFromSinceOursIsMissing != null)
					s = formToCopyFromSinceOursIsMissing.Form;

				if (string.IsNullOrEmpty(s))
				{
					//OK, well even on a non-global language is better than none
					//s = data.TextVariables[key].TextAlternatives.GetFirstAlternative();
					formToCopyFromSinceOursIsMissing = GetFirstAlternativeForm(data.TextVariables[key].TextAlternatives);
					if (formToCopyFromSinceOursIsMissing != null)
						s = formToCopyFromSinceOursIsMissing.Form;
				}
			}

			/* this was a fine idea, execpt that if the user then edits it, well, it's not borrowed anymore but we'll still have this sitting there misleading us
								//record our dubious deed for posterity
								if (formToCopyFromSinceOursIsMissing != null)
								{
									node.SetAttribute("bloom-languageBloomHadToCopyFrom",
													  formToCopyFromSinceOursIsMissing.WritingSystemId);
								}
								 */
			return s;
		}

		public LanguageForm GetFirstAlternativeForm(MultiTextBase alternatives)
		{
			foreach (LanguageForm form in alternatives.Forms)
			{
				if (form.Form.Trim().Length > 0)
				{
					return form;
				}
			}
			return null;
		}



		public void SetLicenseMetdata(Metadata metadata)
		{
			var data = GatherDataItemsFromCollectionSettings(_collectionSettings);
			var itemsToDelete = new HashSet<Tuple<string, string>>();
			GatherDataItemsFromXElement(data,  _dom.RawDom, itemsToDelete);

			string copyright = WebUtility.HtmlEncode(metadata.CopyrightNotice);
			data.UpdateLanguageString("copyright", copyright, "*", false);

			string idOfLanguageUsed;
			string description = metadata.License.GetDescription(_collectionSettings.LicenseDescriptionLanguagePriorities, out idOfLanguageUsed);
			// Don't really have a description for custom license, it returns the RightsStatement for the sake of having something.
			// However, we're already showing that in licenseNotes; if we use it for description too we get duplicate (BL-2198).
			if (metadata.License is CustomLicense)
				description = "";
			data.UpdateLanguageString("licenseDescription", WebUtility.HtmlEncode(description), "en", false);

			string licenseUrl = metadata.License.Url;
			data.UpdateLanguageString("licenseUrl", licenseUrl, "*", false);

			string licenseNotes = metadata.License.RightsStatement;
			data.UpdateLanguageString("licenseNotes", WebUtility.HtmlEncode(licenseNotes), "*", false);

			string licenseImageName = metadata.License.GetImage() == null ? "" : "license.png";
			data.UpdateGenericLanguageString("licenseImage", licenseImageName, false);


			UpdateDomFromDataSet(data, "*", _dom.RawDom, itemsToDelete);

			//UpdateDomFromDataSet() is not able to remove items yet, so we do it explicity

			RemoveDataDivElementIfEmptyValue("licenseDescription", description);
			RemoveDataDivElementIfEmptyValue("licenseImage", licenseImageName);
			RemoveDataDivElementIfEmptyValue("licenseUrl", licenseUrl);
			RemoveDataDivElementIfEmptyValue("copyright", copyright);
			RemoveDataDivElementIfEmptyValue("licenseNotes", licenseNotes);
		}

		private void RemoveDataDivElementIfEmptyValue(string key, string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				foreach (
					XmlElement node in _dom.SafeSelectNodes("//div[@id='bloomDataDiv']//div[@data-book='" + key + "']"))
				{
					node.ParentNode.RemoveChild(node);
				}
			}
		}

		public Metadata GetLicenseMetadata()
		{
			var data = new DataSet();
			GatherDataItemsFromXElement(data, _dom.RawDom);
			var metadata = new Metadata();
			NamedMutliLingualValue d;
			if (data.TextVariables.TryGetValue("copyright", out d))
			{
				metadata.CopyrightNotice = WebUtility.HtmlDecode(d.TextAlternatives.GetFirstAlternative());
			}
			string licenseUrl = "";
			if (data.TextVariables.TryGetValue("licenseUrl", out d))
			{
				licenseUrl = WebUtility.HtmlDecode(d.TextAlternatives.GetFirstAlternative());
			}

			if (licenseUrl == null || licenseUrl.Trim() == "")
			{
				//NB: we are mapping "RightsStatement" (which comes from XMP-dc:Rights) to "LicenseNotes" in the html.
				//custom licenses live in this field, so if we have notes (and no URL) it is a custom one.
				if (data.TextVariables.TryGetValue("licenseNotes", out d))
				{
					string licenseNotes = d.TextAlternatives.GetFirstAlternative();

					metadata.License = new CustomLicense { RightsStatement = WebUtility.HtmlDecode(licenseNotes) };
				}
				else
				{
					// The only remaining current option is a NullLicense
					metadata.License = new NullLicense(); //"contact the copyright owner
				 }
			}
			else // there is a licenseUrl, which means it is a CC license
			{
				metadata.License = CreativeCommonsLicense.FromLicenseUrl(licenseUrl);
				if (data.TextVariables.TryGetValue("licenseNotes", out d))
				{
					metadata.License.RightsStatement = WebUtility.HtmlDecode(d.TextAlternatives.GetFirstAlternative());
				}
			}
			return metadata;
		}

		public string GetVariableOrNull(string key, string writingSystem)
		{
			var f= _dataset.TextVariables.ContainsKey(key)
					   ? _dataset.TextVariables[key].TextAlternatives[writingSystem]
					   : null;

			if (string.IsNullOrEmpty(f))//the TextAlternatives thing gives "", whereas we want null
				return null;
			return f;
		}

		public MultiTextBase GetMultiTextVariableOrEmpty(string key)
		{
			return _dataset.TextVariables.ContainsKey(key)
					   ? _dataset.TextVariables[key].TextAlternatives
					   : new MultiTextBase();
		}

		public Dictionary<string, string> GetWritingSystemCodes()
		{
			return _dataset.WritingSystemAliases;
		}

		private static void SendDataToDebugConsole(DataSet data)
		{
#if DEBUG
			foreach (var item in data.TextVariables)
			{
				foreach (LanguageForm form in item.Value.TextAlternatives.Forms)
				{
					Debug.WriteLine("Gathered: {0}[{1}]={2}", item.Key, form.WritingSystemId, form.Form);
				}
			}
#endif
		}

		private void UpdateTitle(BookInfo info = null)
		{
			NamedMutliLingualValue title;
			if (_dataset.TextVariables.TryGetValue("bookTitleTemplate", out title))
			{
				//NB: In seleting from an ordered shopping list of priority entries, this is only 
				//handling a scenario where a single (title,writingsystem) pair is of interest.
				//That's all we've needed thusfar. But we could imagine needing to work through each one.

				var form = title.TextAlternatives.GetBestAlternative(WritingSystemIdsToTry);

				//allow the title to be a template that pulls in data variables, e.g. "P1 Primer Term{book.term} Week {book.week}"
				foreach (var dataItem in _dataset.TextVariables)
				{
					form.Form = form.Form.Replace("{" + dataItem.Key + "}", dataItem.Value.TextAlternatives.GetBestAlternativeString(WritingSystemIdsToTry));
				}

				_dom.Title = form.Form;
				if (info != null)
					info.Title =form.Form.Replace("<br />", ""); // Clean out breaks inserted at newlines.

				this.Set("bookTitle", form.Form, form.WritingSystemId);
				
			}
			else if (_dataset.TextVariables.TryGetValue("bookTitle", out title))
			{
				var t = title.TextAlternatives.GetBestAlternativeString(WritingSystemIdsToTry);
				_dom.Title = t;
				if (info != null)
				{
					info.Title = TextOfInnerHtml(t.Replace("<br />", "")); // Clean out breaks inserted at newlines.
					// Now build the AllTitles field
					var sb = new StringBuilder();
					sb.Append("{");
					foreach (var langForm in title.TextAlternatives.Forms)
					{
						if (sb.Length > 1)
							sb.Append(",");
						sb.Append("\"");
						sb.Append(langForm.WritingSystemId);
						sb.Append("\":\"");
						sb.Append(TextOfInnerHtml(langForm.Form).Replace("\\", "\\\\").Replace("\"", "\\\"")); // Escape backslash and double-quote
						sb.Append("\"");
					}
					sb.Append("}");
					info.AllTitles = sb.ToString();
				}
			}
		}

		/// <summary>
		/// The data we extract into title fields of _dataSet is the InnerXml of some XML node.
		/// This might have markup, e.g., making a word italic. It will also have the amp, lt, and gt escaped.
		/// We want to reduce it to plain text to store in bookInfo.
		/// </summary>
		/// <param name="input"></param>
		/// <returns></returns>
		internal static string TextOfInnerHtml(string input)
		{
			// Parsing it as XML and then extracting the value removes any markup.
			var doc = XElement.Parse("<doc>" + input + "</doc>");
			return doc.Value;
		}

		private string[] WritingSystemIdsToTry
		{
			get
			{
				return new string[] {_collectionSettings.Language1Iso639Code??"", _collectionSettings.Language2Iso639Code??"", _collectionSettings.Language3Iso639Code ?? "", "en", "fr", "th", "pt", "*" };
			}
		}


		public void SetMultilingualContentLanguages(string language2Code, string language3Code)
		{
			if (language2Code == _collectionSettings.Language1Iso639Code) //can't have the vernacular twice
				language2Code = null;
			if (language3Code == _collectionSettings.Language1Iso639Code)
				language3Code = null;
			if (language2Code == language3Code)	//can't use the same lang twice
				language3Code = null;

			if (String.IsNullOrEmpty(language2Code))
			{
				if (!String.IsNullOrEmpty(language3Code))
				{
					language2Code = language3Code; //can't have a 3 without a 2
					language3Code = null;
				}
				else
					language2Code = null;
			}
			if (language3Code == "")
				language3Code = null;

			Set("contentLanguage2", language2Code,false);
			Set("contentLanguage3", language3Code, false);
		}

//        public IEnumerable<KeyValuePair<string,NamedMutliLingualValue>>  GetCollectionVariables()
//        {
//            return from v in this._dataset.TextVariables where v.Value.IsCollectionValue select v;
//        }
	}
}
