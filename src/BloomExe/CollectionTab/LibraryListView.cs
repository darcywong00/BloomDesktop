﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Bloom.Book;
using Bloom.Collection;
using Bloom.Properties;
using Bloom.WebLibraryIntegration;
using Bloom.Workspace;
using DesktopAnalytics;
using Palaso.Reporting;
using L10NSharp;
using Palaso.IO;

namespace Bloom.CollectionTab
{
	public partial class LibraryListView : UserControl
	{
		private const int ButtonHeight = 112;
		private const int ButtonWidth = 92;

		public delegate LibraryListView Factory();//autofac uses this

		private readonly LibraryModel _model;
		private readonly BookSelection _bookSelection;
		private readonly HistoryAndNotesDialog.Factory _historyAndNotesDialogFactory;
		private Font _headerFont;
		private Font _editableBookFont;
		private Font _collectionBookFont;
		private bool _thumbnailRefreshPending;
		private BookTransfer _bookTransferrer;
		private DateTime _lastClickTime;
		private bool _primaryCollectionReloadPending;
		private bool _disposed;
		private BookCollection _downloadedBookCollection;
		enum ButtonManagementStage
		{
			LoadPrimary, ImprovePrimary, LoadSourceCollections, ImproveAndRefresh
		}

		private ButtonManagementStage _buttonManagementStage = ButtonManagementStage.LoadPrimary;

		/// <summary>
		/// we go through these at idle time, doing slow things like actually instantiating the book to get the title in preferred language
		/// A stack would be better for updating "the thing I just changed", but we're using a queue at the moment simply because we
		/// want you'd see at the top of the screen to update before what's at the bottom or offscreen
		/// </summary>
		private readonly ConcurrentQueue<ButtonInfo> _buttonsNeedingSlowUpdate;

		private bool _alreadyReportedErrorDuringImproveAndRefreshBookButtons;

		public LibraryListView(LibraryModel model, BookSelection bookSelection, SelectedTabChangedEvent selectedTabChangedEvent, LocalizationChangedEvent localizationChangedEvent,
			HistoryAndNotesDialog.Factory historyAndNotesDialogFactory, BookTransfer bookTransferrer)
		{
			_model = model;
			_bookSelection = bookSelection;
			localizationChangedEvent.Subscribe(unused=>LoadSourceCollectionButtons());
			_historyAndNotesDialogFactory = historyAndNotesDialogFactory;
			_bookTransferrer = bookTransferrer;
			_buttonsNeedingSlowUpdate = new ConcurrentQueue<ButtonInfo>();
			selectedTabChangedEvent.Subscribe(OnSelectedTabChanged);
			InitializeComponent();
			_primaryCollectionFlow.HorizontalScroll.Visible = false;

			_primaryCollectionFlow.Controls.Clear();
			_primaryCollectionFlow.HorizontalScroll.Visible = false;
			_sourceBooksFlow.Controls.Clear();
			_sourceBooksFlow.HorizontalScroll.Visible = false;

			if (!_model.ShowSourceCollections)
			{
				splitContainer1.Panel2Collapsed = true;
			}

			_headerFont = new Font(SystemFonts.DialogFont.FontFamily, (float)10.0, FontStyle.Bold);
			_editableBookFont = new Font(SystemFonts.DialogFont.FontFamily, (float)9.0);//, FontStyle.Bold);
			_collectionBookFont = new Font(SystemFonts.DialogFont.FontFamily, (float)9.0);

			//enhance: move to model
			bookSelection.SelectionChanged += new EventHandler(OnBookSelectionChanged);

			_settingsProtectionHelper.ManageComponent(_openFolderOnDisk);

			_showHistoryMenu.Visible = _showNotesMenu.Visible = Settings.Default.ShowSendReceive;

			if(Settings.Default.ShowExperimentalCommands)
				_settingsProtectionHelper.ManageComponent(_exportToXMLForInDesignToolStripMenuItem);//we are restriting it because it opens a folder from which the user could do damage
			_exportToXMLForInDesignToolStripMenuItem.Visible = Settings.Default.ShowExperimentalCommands;
		}

		private void OnExportToXmlForInDesign(object sender, EventArgs e)
		{

			using(var d = new InDesignXmlInformationDialog())
			{
				d.ShowDialog();
			}
			using (var dlg = new SaveFileDialog())
			{
				dlg.FileName = Path.GetFileNameWithoutExtension(SelectedBook.GetPathHtmlFile())+".xml";
				dlg.InitialDirectory = SelectedBook.FolderPath;
				if(DialogResult.OK == dlg.ShowDialog())
				{
					try
					{
						_model.ExportInDesignXml(dlg.FileName);
						PathUtilities.SelectFileInExplorer(dlg.FileName);
						Analytics.Track("Exported XML For InDesign");
					}
					catch (Exception error)
					{
						Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "Could not export the book to XML");
						Analytics.ReportException(error);
					}
				}
			}
		}

		private void OnBookSelectionChanged(object sender, EventArgs e)
		{
			if (sender == null) return;

			var selection = (BookSelection)sender;
			if ((selection.CurrentSelection != null) && (selection.CurrentSelection.BookInfo != null))
			{
				HighlightBookButton(selection.CurrentSelection.BookInfo);					
			}
		}

		public int PreferredWidth
		{
			get { return 300; }
		}

		protected override void OnLoad(EventArgs e)
		{
			base.OnLoad(e);
			Application.Idle += ManageButtonsAtIdleTime;
		}


		private void ManageButtonsAtIdleTime(object sender, EventArgs e)
		{
			if (_disposed) //could happen if a version update was detected on app launch
				return;

			switch (_buttonManagementStage)
			{
				case ButtonManagementStage.LoadPrimary:
					LoadPrimaryCollectionButtons();
					_buttonManagementStage = ButtonManagementStage.ImprovePrimary;
					_primaryCollectionFlow.Refresh();
					break;

				//here we do any expensive fix up of the buttons in the primary collection (typically, getting vernacular captions, which requires reading their html)
				case ButtonManagementStage.ImprovePrimary:
					if (_buttonsNeedingSlowUpdate.IsEmpty)
					{
						_buttonManagementStage = ButtonManagementStage.LoadSourceCollections;
					}
					else
					{
						ImproveAndRefreshBookButtons();
					}
					break;
				case ButtonManagementStage.LoadSourceCollections:
					LoadSourceCollectionButtons();
					_buttonManagementStage = ButtonManagementStage.ImproveAndRefresh;
					if (Program.PathToBookDownloadedAtStartup != null)
					{
						// We started up with a command to downloaded a book...Select it.
						SelectBook(new BookInfo(Program.PathToBookDownloadedAtStartup, false));
					}
					break;
				case ButtonManagementStage.ImproveAndRefresh:
					ImproveAndRefreshBookButtons();
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		/// <summary>
		/// the primary could as well be called "the one editable collection"... the one at the top
		/// </summary>
		private void LoadPrimaryCollectionButtons()
		{
			_primaryCollectionReloadPending = false;
			_primaryCollectionFlow.SuspendLayout();
			_primaryCollectionFlow.Controls.Clear();
			//without this guy, the FLowLayoutPanel uses the height of a button, on *the next row*, for the height of this row!
			var invisibleHackPartner = new Label() {Text = "", Width = 0};
			_primaryCollectionFlow.Controls.Add(invisibleHackPartner);
			var primaryCollectionHeader = new ListHeader() {ForeColor = Palette.TextAgainstDarkBackground};
			primaryCollectionHeader.Label.Text = _model.VernacularLibraryNamePhrase;
			primaryCollectionHeader.AdjustWidth();
			_primaryCollectionFlow.Controls.Add(primaryCollectionHeader);
			//_primaryCollectionFlow.SetFlowBreak(primaryCollectionHeader, true);
			_primaryCollectionFlow.Controls.Add(_menuTriangle);//NB: we're using a picture box instead of a button because the former can have transparency.
			LoadOneCollection(_model.GetBookCollections().First(), _primaryCollectionFlow);
			_primaryCollectionFlow.ResumeLayout();
		}

		private void LoadSourceCollectionButtons()
		{
			if (!_model.ShowSourceCollections)
			{
				_sourceBooksFlow.Visible = false;
				string lockNotice = L10NSharp.LocalizationManager.GetString("CollectionTab.bookSourcesLockNotice",
																			   "This collection is locked, so new books cannot be added/removed.");

				var lockNoticeLabel = new Label()
					{
						Text = lockNotice,
						Size = new Size(_primaryCollectionFlow.Width - 20, 15),
						ForeColor = Palette.TextAgainstDarkBackground,
						Padding = new Padding(10, 0, 0, 0)
					};
				_primaryCollectionFlow.Controls.Add(lockNoticeLabel);
				return;
			}

			var collections = _model.GetBookCollections();
			//without this guy, the FLowLayoutPanel uses the height of a button, on *the next row*, for the height of this row!
			var invisibleHackPartner = new Label() {Text = "", Width = 0};

			_sourceBooksFlow.SuspendLayout();
			_sourceBooksFlow.Controls.Clear();
			var bookSourcesHeader = new ListHeader() { ForeColor = Palette.TextAgainstDarkBackground, Width = 450 };

			string shellSourceHeading = L10NSharp.LocalizationManager.GetString("CollectionTab.sourcesForNewShellsHeading",
																				"Sources For New Shells");
			string bookSourceHeading = L10NSharp.LocalizationManager.GetString("CollectionTab.bookSourceHeading",
																			   "Sources For New Books");
			bookSourcesHeader.Label.Text = _model.IsShellProject ? shellSourceHeading : bookSourceHeading;
			// Don't truncate the heading: see https://jira.sil.org/browse/BL-250.
			if (bookSourcesHeader.Width < bookSourcesHeader.Label.Width)
				bookSourcesHeader.Width = bookSourcesHeader.Label.Width;
			invisibleHackPartner = new Label() {Text = "", Width = 0};
			_sourceBooksFlow.Controls.Add(invisibleHackPartner);
			_sourceBooksFlow.Controls.Add(bookSourcesHeader);
			_sourceBooksFlow.SetFlowBreak(bookSourcesHeader, true);


			foreach (BookCollection collection in collections.Skip(1))
			{
				if (_sourceBooksFlow.Controls.Count > 0)
					_sourceBooksFlow.SetFlowBreak(_sourceBooksFlow.Controls[_sourceBooksFlow.Controls.Count - 1], true);

				int indexForHeader = _sourceBooksFlow.Controls.Count;
				if (LoadOneCollection(collection, _sourceBooksFlow))
				{
					//without this guy, the FLowLayoutPanel uses the height of a button, on *the next row*, for the height of this row!
					invisibleHackPartner = new Label() {Text = "", Width = 0};
					_sourceBooksFlow.Controls.Add(invisibleHackPartner);
					_sourceBooksFlow.Controls.SetChildIndex(invisibleHackPartner, indexForHeader);

					//We showed at least one book, so now go back and insert the header
					var collectionHeader = new Label()
						{
							Text = L10NSharp.LocalizationManager.GetDynamicString("Bloom", "CollectionTab." + collection.Name, collection.Name),
							Size = new Size(_sourceBooksFlow.Width - 20, 20),
							ForeColor = Palette.TextAgainstDarkBackground,
							Padding = new Padding(10, 0, 0, 0)
						};
					collectionHeader.Margin = new Padding(0, 10, 0, 0);
					collectionHeader.Font = _headerFont;
					_sourceBooksFlow.Controls.Add(collectionHeader);
					_sourceBooksFlow.Controls.SetChildIndex(collectionHeader, indexForHeader + 1);
					_sourceBooksFlow.SetFlowBreak(collectionHeader, true);
				}
			}

			AddFinalLinks();
			_sourceBooksFlow.ResumeLayout();
		}

		private void AddFinalLinks()
		{
			// Nothing to do currently. This was used to display the missing books link in a source collection.
		}

		/// <summary>
		/// Called at idle time after everything else is set up, and only when this tab is visible
		/// </summary>
		private void ImproveAndRefreshBookButtons()
		{
			ButtonInfo buttonInfo;
			if (!_buttonsNeedingSlowUpdate.TryDequeue(out buttonInfo))
				return;

			Button button = buttonInfo.Button;
			BookInfo bookInfo = button.Tag as BookInfo;
			Book.Book book;
			try
			{
				book = _model.GetBookFromBookInfo(bookInfo);
			}
			catch (Exception error)
			{
				//skip over the dependency injection layer
				if (error.Source == "Autofac" && error.InnerException != null)
					error = error.InnerException;
				Logger.WriteEvent("There was a problem with the book at " + bookInfo.FolderPath + ". " + error.Message);
				if (!_alreadyReportedErrorDuringImproveAndRefreshBookButtons)
				{
					_alreadyReportedErrorDuringImproveAndRefreshBookButtons = true;
					Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "There was a problem with the book at {0}. \r\n\r\nClick the 'Details' button for more information.\r\n\r\nThis error may effect other books, but this is the only notice you will receive.\r\n\r\nSee 'Help:Show Event Log' for any further errors.", bookInfo.FolderPath);
				}
				return;
			}

			//Only go looking for a better title if the book hasn't already been localized when we first showed it.
			//The idea is, if we already have a localization mapping for this name, then
			// we're not going to get a better title by digging into the document itself and overriding what the localizer
			// chose to call it.
			// Note: currently (August 2014) the books that will have been localized are are those in the main "templates" section: Basic Book, Calendar, etc.
			if (button.Text == ShortenTitleIfNeeded(bookInfo.QuickTitleUserDisplay, button))
			{
				var bestTitle = book.TitleBestForUserDisplay;
				var titleBestForUserDisplay = ShortenTitleIfNeeded(bestTitle, button);
				if (titleBestForUserDisplay != button.Text)
				{
					Debug.WriteLine(button.Text + " --> " + titleBestForUserDisplay);
					button.Text = titleBestForUserDisplay;
					toolTip1.SetToolTip(button, bestTitle);
				}
			}
			if (buttonInfo.ThumbnailRefreshNeeded)//!bookInfo.TryGetPremadeThumbnail(out unusedImage))
				ScheduleRefreshOfOneThumbnail(book);
		}

		void OnBloomLibrary_Click(object sender, EventArgs e)
		{
			if (_model.IsShellProject)
			{
				// Display dialog making sure they know what they're doing
				var dialogResult = ShowBloomLibraryLinkVerificationDialog();
				if (dialogResult != DialogResult.OK)
					return;
			}
			Process.Start(BookTransfer.UseSandbox
				? "http://dev.bloomlibrary.org/books"
				: "http://bloomlibrary.org/books");
		}

		DialogResult ShowBloomLibraryLinkVerificationDialog()
		{
			var dlg = new BloomLibraryLinkVerification();
			return dlg.GetVerification(this);
		}

		/// <summary>
		///
		/// </summary>
		/// <returns>True if the collection should be shown</returns>
		private bool LoadOneCollection(BookCollection collection, FlowLayoutPanel flowLayoutPanel)
		{
			collection.CollectionChanged += OnCollectionChanged;
			bool loadedAtLeastOneBook = false;
			foreach (Book.BookInfo bookInfo in collection.GetBookInfos())
			{
				try
				{
					if (!bookInfo.IsExperimental || Settings.Default.ShowExperimentalBooks)
					{
						loadedAtLeastOneBook = true;
						AddOneBook(bookInfo, flowLayoutPanel, collection.Name.ToLowerInvariant() == "templates");
					}
				}
				catch (Exception error)
				{
					Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "Could not load the book at " + bookInfo.FolderPath);
				}
			}
			if (collection.Name == BookCollection.DownloadedBooksCollectionNameInEnglish)
			{
				_downloadedBookCollection = collection;
				collection.FolderContentChanged += DownLoadedBooksChanged;
				collection.WatchDirectory(); // In case another instance downloads a book.
				var bloomLibrayLink = new LinkLabel()
				{
					Text =
						L10NSharp.LocalizationManager.GetString("CollectionTab.bloomLibraryLinkLabel",
																"Get more source books at BloomLibrary.org",
																"Shown at the bottom of the list of books. User can click on it and it will attempt to open a browser to show the Bloom Library"),
					Width = 400,
					Margin = new Padding(17, 0, 0, 0),
					LinkColor = Palette.TextAgainstDarkBackground
				};
				bloomLibrayLink.Click += new EventHandler(OnBloomLibrary_Click);
				flowLayoutPanel.Controls.Add(bloomLibrayLink);
				return true;
			}
			return loadedAtLeastOneBook;
		}

	   private bool IsSuitableSourceForThisEditableCollection(BookInfo bookInfo)
		{
			return (_model.IsShellProject && bookInfo.IsSuitableForMakingShells) ||
				   (!_model.IsShellProject && bookInfo.IsSuitableForVernacularLibrary);
		}

		private Timer _newDownloadTimer;
		/// <summary>
		/// Called when a file system watcher notices a new book (or some similar change) in our downloaded books folder.
		/// This will happen on a thread-pool thread.
		/// Since we are updating the UI in response we want to deal with it on the main thread.
		/// This also has the effect that it can't happen in the middle of another LoadSourceCollectionButtons().
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="eventArgs"></param>
		private void DownLoadedBooksChanged(object sender, ProjectChangedEventArgs eventArgs)
		{
			Invoke((Action) (() =>
			{
				// We may notice a change to the downloaded books directory before the other Bloom instance has finished
				// copying the new book there. Finishing should not take long, because the download is done...at worst
				// we have to copy the book on our own filesystem. Typically we only have to move the directory.
				// As a safeguard, wait half a second before we update things.
				if (_newDownloadTimer != null)
				{
					// Things changed again before we started the update! Forget the old update and wait until things
					// are stable for the required interval.
					_newDownloadTimer.Stop();
					_newDownloadTimer.Dispose();
				}
				_newDownloadTimer = new Timer();
				_newDownloadTimer.Tick += (o, args) =>
				{
					_newDownloadTimer.Stop();
					_newDownloadTimer.Dispose();
					_newDownloadTimer = null;

					UpdateDownloadedBooks(eventArgs.Path);
				};
				_newDownloadTimer.Interval = 500;
				_newDownloadTimer.Start();
			}));
		}

		private void UpdateDownloadedBooks(string pathToChangedBook)
		{
			var newBook = new BookInfo(pathToChangedBook, false);
			// It's always worth reloading...maybe we didn't have a button before because it was not
			// suitable for making vernacular books, but now it is! Or maybe the metadata changed some
			// other way...we want the button to have valid metadata for the book.
			// Optimize: maybe it would be worth trying to find the right place to insert or replace just one button?
			LoadSourceCollectionButtons();
			if (Enabled && CollectionTabIsActive)
				SelectBook(newBook);
		}

		/// <summary>
		/// Tells whether the collections tab is visible. If it isn't, we don't try to switch to show the selected book.
		/// In the current configuration, Parent.Parent.Parent is the LibraryView; this is added and removed from
		/// the higher level view depending on whether it is wanted, so if it has no higher parent it is hidden
		/// (although Visible is still true!) and we should not try to switch.
		/// One day we may enhance it so that we switch tabs and show it, but there are states where that would
		/// be dangerous.
		/// </summary>
		private bool CollectionTabIsActive
		{
			get { return Parent.Parent.Parent.Parent != null; }
		}

		private void AddOneBook(BookInfo bookInfo, FlowLayoutPanel flowLayoutPanel, bool localizeTitle)
		{
			string title = bookInfo.QuickTitleUserDisplay;
			if (localizeTitle)
				title = LocalizationManager.GetDynamicString("Bloom", "Template." + title, title);

			var button = new Button
			{
				Size = new Size(ButtonWidth, ButtonHeight),
				Font = bookInfo.IsEditable ? _editableBookFont : _collectionBookFont,
				TextImageRelation = TextImageRelation.ImageAboveText,
				ImageAlign = ContentAlignment.TopCenter,
				TextAlign = ContentAlignment.BottomCenter,
				FlatStyle = FlatStyle.Flat,
				ForeColor = Palette.TextAgainstDarkBackground,
				UseMnemonic = false, //otherwise, it tries to interpret '&' as a shortcut
				ContextMenuStrip = _bookContextMenu,
				AutoSize = false,

				Tag = bookInfo
			};

			button.MouseDown += OnClickBook; //we need this for right-click menu selection, which needs to 1st select the book
			//doesn't work: item.DoubleClick += (sender,arg)=>_model.DoubleClickedBook();
			
			button.Text = ShortenTitleIfNeeded(title, button);
			button.FlatAppearance.BorderSize = 1;
			button.FlatAppearance.BorderColor = BackColor;

			toolTip1.SetToolTip(button, title);

			Image thumbnail = Resources.PagePlaceHolder;
			_bookThumbnails.Images.Add(bookInfo.Id, thumbnail);
			button.ImageIndex = _bookThumbnails.Images.Count - 1;
			flowLayoutPanel.Controls.Add(button); // important to add it before RefreshOneThumbnail; uses parent flow to decide whether primary

			// Can't use this test until after we add button (uses parent info)
			if (!IsUsableBook(button))
				button.ForeColor = Palette.DisabledTextAgainstDarkBackColor;

			Image img;
			var refreshThumbnail = false;
			//review: we could do this at idle time, too:
			if (bookInfo.TryGetPremadeThumbnail(out img))
			{
				RefreshOneThumbnail(bookInfo, img);
			}
			else
			{
				//show this one for now, in the background someone will do the slow work of getting us a better one
				RefreshOneThumbnail(bookInfo,Resources.placeHolderBookThumbnail);
				refreshThumbnail = true;
			}
			_buttonsNeedingSlowUpdate.Enqueue(new ButtonInfo(button, refreshThumbnail));
		}

		private string ShortenTitleIfNeeded(string title, Button button)
		{
			var maxHeight = ButtonHeight - HtmlThumbNailer.ThumbnailOptions.DefaultHeight - (button.FlatAppearance.BorderSize * 2);

			// -2 because the text will wrap if there is not at least one pixel between the text and the border
			var width = button.Width - button.Margin.Horizontal - (button.FlatAppearance.BorderSize * 2) - 2;

			var targetSize = new Size(width, int.MaxValue);
			// WordBreak is necessary for sensible measurment of line widths...otherwise it ignores the width
			// constraint and puts all the text on one line.
			// NoPrefix suppresses special treatment of ampersand.
			var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
			var source = title;
			var firstLine = ""; // May be used if the first line starts with a long word; see below.
			using (var g = this.CreateGraphics())
			{
				var size = TextRenderer.MeasureText(g, source, button.Font, targetSize, flags);
				if (size.Height <= maxHeight && size.Width <= width)
					return source;
				var tooBig = source;
				var fits = source.Substring(0, 4); // include something not entirely trivial
				for (int i = 0; i < 2; i++) // trick to get a second iteration for long word on first line
				{
					while (tooBig.Length > fits.Length + 1)
					{
						var probe = source.Substring(0, (tooBig.Length + fits.Length)/2);
						size = TextRenderer.MeasureText(g, probe + "…", button.Font, targetSize, flags);
						if (size.Height <= maxHeight && size.Width <= width)
							fits = probe;
						else
							tooBig = probe;
					}
					if (i == 0 && size.Height <= maxHeight/2)
					{
						// Pesky TextRenderer won't break long words, but button layout code will.
						// If we got a long word on first line, the algorithm above will truncate
						// all the way down to ONE line. See if we can fit some more on the second line.
						// (Note that we don't need to consider the case of a one-line result that
						// contains white space. If it's possible to put even one short word on the
						// first line, that's what the TextRenderer and the button layout code both do.)

						// Enhance: this fix assumes that we are only showing two lines, even though
						// most of the code here is designed to be more general. If there is room for
						// three or more lines, the current code could still truncate to two if there
						// is a long word on the second. It's somewhat tricky to make it handle n lines:
						// we start to really need to know the line height to tell whether truncation
						// happened. The space that's available for more lines may not be exactly
						// the total minus the height of the first line. I decided to apply YAGNI.
						maxHeight -= size.Height;
						firstLine = fits;
						source = source.Substring(firstLine.Length);
						// Rather arbitrary, but 4 are pretty sure to fit, and trying to measure an
						// empty string might be a problem.
						if (source.Length > 4)
							fits = source.Substring(0, 4);
						else
							fits = source;

						tooBig = source;
					}
					else
					{
						// Already iterated, or what we have already takes two lines
						// (maybe the long word was on the second line).
						break;
					}
				}
				return firstLine + fits + "…";
			}
		}

		/// <summary>
		/// Make the result look like it's on a colored paper, or make it transparent for composing on top
		/// of some other image.
		/// </summary>
		private ImageAttributes MagentaToPaperColor(Color paperColor)
		{
			ImageAttributes imageAttributes = new ImageAttributes();
			ColorMap map = new ColorMap();
			map.OldColor =  Color.Magenta;
			map.NewColor = paperColor;
			imageAttributes.SetRemapTable(new ColorMap[] {map});
			return imageAttributes;
		}

		private void OnCollectionChanged(object sender, EventArgs e)
		{
			_primaryCollectionReloadPending = true;
		}

		private void OnClickBook(object sender, EventArgs e)
		{
			if (!IsUsableBook((Button) sender))
			{
				MessageBox.Show(LocalizationManager.GetString("CollectionTab.hiddenBookExplanationForSourceCollections", "Because this is a source collection, Bloom isn't offering any existing shells as sources for new shells. If you want to add a language to a shell, instead you need to edit the collection containing the shell, rather than making a copy of it. Also, the Wall Calendar currently can't be used to make a new Shell."));
				return;
			}
			BookInfo bookInfo = ((Button)sender).Tag as BookInfo;
			if (bookInfo == null)
				return;

			var lastClickTime = _lastClickTime;
			_lastClickTime = DateTime.Now;

			try
			{
				if (SelectedBook != null && bookInfo == SelectedBook.BookInfo)
				{
					//I couldn't get the DoubleClick event to work, so I rolled my own
					if (Control.MouseButtons == MouseButtons.Left &&
						DateTime.Now.Subtract(lastClickTime).TotalMilliseconds < SystemInformation.DoubleClickTime)
					{
						_model.DoubleClickedBook();
					}
					return; // already selected, nothing to do.
				}
			}
			catch (Exception error) // Review: is this needed now bulk of method refactored into SelectBook?
			{
				//skip over the dependency injection layer
				if (error.Source == "Autofac" && error.InnerException != null)
					error = error.InnerException;

				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "Bloom cannot display that book.");
			}
			SelectBook(bookInfo);
		}

		private void HighlightBookButton(BookInfo bookInfo)
		{
			foreach (var btn in AllBookButtons())
			{
				if (btn.Tag == bookInfo)
					btn.FlatAppearance.BorderColor = Palette.TextAgainstDarkBackground;
				else
					btn.FlatAppearance.BorderColor = BackColor;
			}
		}

		private void SelectBook(BookInfo bookInfo)
		{
			try
			{
				_bookSelection.SelectBook(_model.GetBookFromBookInfo(bookInfo));

				_bookContextMenu.Enabled = true;
				//Debug.WriteLine("before selecting " + SelectedBook.Title);
				_model.SelectBook(SelectedBook);
				//Debug.WriteLine("after selecting " + SelectedBook.Title);
				//didn't help: _listView.Focus();//hack we were losing clicks
				SelectedBook.ContentsChanged -= new EventHandler(OnContentsOfSelectedBookChanged); //in case we're already subscribed
				SelectedBook.ContentsChanged += new EventHandler(OnContentsOfSelectedBookChanged);

				deleteMenuItem.Enabled = _model.CanDeleteSelection;
				_updateThumbnailMenu.Visible = _model.CanUpdateSelection;
				exportToWordOrLibreOfficeToolStripMenuItem.Visible = _model.CanExportSelection;
				_updateFrontMatterToolStripMenu.Visible = _model.CanUpdateSelection;
			}
			catch (Exception error)
			{
				//skip over the dependency injection layer
				if (error.Source == "Autofac" && error.InnerException != null)
					error = error.InnerException;

				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "Bloom cannot display that book.");
			}
		}

		private Book.Book SelectedBook
		{
			set
			{
				foreach (var btn in AllBookButtons())
				{
					btn.BackColor = btn.Tag==value ? Color.DarkGray : _primaryCollectionFlow.BackColor;
				}
			}
			get { return _bookSelection.CurrentSelection; }
		}

		private Button SelectedButton
		{
			get
			{
				return AllBookButtons().FirstOrDefault(b => b.Tag == SelectedBook.BookInfo);
			}
		}

		/// <summary>
		/// The image to show on the cover might have changed. Just make a note ot re-show it next time we're visible
		/// </summary>
		private void OnContentsOfSelectedBookChanged(object sender, EventArgs e)
		{
			_thumbnailRefreshPending = true;
		}

		private void OnBackColorChanged(object sender, EventArgs e)
		{
			_primaryCollectionFlow.BackColor = BackColor;
		}

		private void OnSelectedTabChanged(TabChangedDetails obj)
		{
			if(obj.To is LibraryView)
			{
				Application.Idle -= ManageButtonsAtIdleTime;
				Application.Idle += ManageButtonsAtIdleTime;
				Book.Book book = SelectedBook;
				if (book != null && SelectedButton != null)
				{
					var bestTitle = book.TitleBestForUserDisplay;
					SelectedButton.Text = ShortenTitleIfNeeded(bestTitle, SelectedButton);
					toolTip1.SetToolTip(SelectedButton, bestTitle);
					if (_thumbnailRefreshPending)
					{
						_thumbnailRefreshPending = false;
						ScheduleRefreshOfOneThumbnail(book);
					}
				}
				if (_primaryCollectionReloadPending)
				{
					LoadPrimaryCollectionButtons();
					// One reason to reload is that we created a new book. We need to go through the steps of selecting it
					// so that e.g. its menu options are properly configured.
					if (SelectedBook != null)
						SelectBook(SelectedBook.BookInfo);
				}
			}
			else
			{
				Application.Idle -= ManageButtonsAtIdleTime;
			}
		}


		private void RefreshOneThumbnail(Book.BookInfo bookInfo, Image image)
		{
			if (IsDisposed)
				return;
			try
			{
				var imageIndex = _bookThumbnails.Images.IndexOfKey(bookInfo.Id);
				if (imageIndex > -1)
				{
					_bookThumbnails.Images[imageIndex] = image;
					var button = FindBookButton(bookInfo);
					button.Image = IsUsableBook(button) ? image : MakeDim(image);
				}
			}

			catch (Exception e)
			{
				Logger.WriteEvent("Error refreshing thumbnail. "+e.Message);
#if DEBUG
				throw;
#endif
			}
		}

		bool IsUsableBook(Button bookButton)
		{
			// We'd prefer to use collection.Type == BookCollection.CollectionType.TheOneEditableCollection)
			// but we don't have access to the collection at all the points where we need to evaluate this.
			// Depending on the parent like this unfortunately means we can't use this method until the button
			// has its parent.
			// Eithe way, the basic idea is that books in the main collection you are now editing are always usable.
			if (bookButton.Parent == _primaryCollectionFlow)
				return true;
			var bookInfo = (BookInfo) bookButton.Tag;
			return IsSuitableSourceForThisEditableCollection(bookInfo);
		}

		// Adapted from http://tech.pro/tutorial/660/csharp-tutorial-convert-a-color-image-to-grayscale
		// Author claims this is about 20x faster than manipulating pixels directly (62 vs 1135ms for some image on some hardware).
		public static Bitmap MakeDim(Image original)
		{
			//create a blank bitmap the same size as original
			Bitmap newBitmap = new Bitmap(original.Width, original.Height);

			//get a graphics object from the new image
			using (Graphics g = Graphics.FromImage(newBitmap))
			{
				//create the grayscale ColorMatrix
				var colorMatrix = new ColorMatrix(
					new float[][]
					{
						// convert to greyscale: this (original) version leaves them too bright, and the distinction may be lost on color-blind
						//new float[] {.3f, .3f, .3f, 0, 0},
						//new float[] {.59f, .59f, .59f, 0, 0},
						//new float[] {.11f, .11f, .11f, 0, 0},
						//new float[] {0, 0, 0, 1, 0},
						//new float[] {0, 0, 0, 0, 1}

						// halve all color values to make darker--very similar to the chosen variant, but dark colors are strengthened.
						//new float[] {0.5f, 0, 0, 0, 0},
						//new float[] {0, 0.5f, 0, 0, 0},
						//new float[] {0, 0, 0.5f, 0, 0},
						//new float[] {0, 0, 0, 1, 0},
						//new float[] {0, 0, 0, 0, 1}

						// make it semi-transparent; this reduces contrast with background for all colors.
						new float[] {1.0f, 0, 0, 0, 0},
						new float[] {0, 1.0f, 0, 0, 0},
						new float[] {0, 0, 1.0f, 0, 0},
						new float[] {0, 0, 0, 0.4f, 0}, // the 0.4 here is what really does it.
						new float[] {0, 0, 0, 0, 1}
					});

				ImageAttributes attributes = new ImageAttributes();
				attributes.SetColorMatrix(colorMatrix);

				//draw the original image on the new image using the color matrix to adapt the colors
				g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
					0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
			}
			return newBitmap;
		}

		private Button FindBookButton(Book.BookInfo bookInfo)
		{
			return AllBookButtons().FirstOrDefault(b => b.Tag == bookInfo);
		}

		private IEnumerable<Button> AllBookButtons()
		{
			foreach(var btn in _primaryCollectionFlow.Controls.OfType<Button>())
			{
				yield return btn;
			}

			foreach (var btn in _sourceBooksFlow.Controls.OfType<Button>())
			{
				yield return btn;
			}
		}

		private void ScheduleRefreshOfOneThumbnail(Book.Book book)
		{
			_model.UpdateThumbnailAsync(book, new HtmlThumbNailer.ThumbnailOptions(), RefreshOneThumbnail, HandleThumbnailerErrror);
		}

		private void HandleThumbnailerErrror(Book.BookInfo bookInfo, Exception error)
		{
			RefreshOneThumbnail(bookInfo, Resources.Error70x70);
		}

		private void deleteMenuItem_Click(object sender, EventArgs e)
		{
			var button = AllBookButtons().FirstOrDefault(b => b.Tag == SelectedBook.BookInfo);
			if (_model.DeleteBook(SelectedBook))
			{
				Debug.Assert(button != null && _primaryCollectionFlow.Controls.Contains(button));
				if (button != null && _primaryCollectionFlow.Controls.Contains(button))
				{
					_primaryCollectionFlow.Controls.Remove(button);
				}
			}
		}

		private void _updateThumbnailMenu_Click(object sender, EventArgs e)
		{
			ScheduleRefreshOfOneThumbnail(SelectedBook);
		}

		private void OnBringBookUpToDate_Click(object sender, EventArgs e)
		{
			try
			{
				_model.BringBookUpToDate();
			}
			catch (Exception error)
			{
				var msg = LocalizationManager.GetDynamicString("Bloom", "Errors.ErrorUpdating",
					"There was a problem updating the book.  Restarting Bloom may fix the problem.  If not, please click the 'Details' button and report the problem to the Bloom Developers.");
				ErrorReport.NotifyUserOfProblem(error, msg);
			}
		}

		private void _openFolderOnDisk_Click(object sender, EventArgs e)
		{
			_model.OpenFolderOnDisk();
		}

		private void OnOpenAdditionalCollectionsFolderClick(object sender, EventArgs e)
		{
			PathUtilities.OpenDirectoryInExplorer(ProjectContext.GetInstalledCollectionsDirectory());
		}

		private void OnVernacularProjectHistoryClick(object sender, EventArgs e)
		{
			using(var dlg = _historyAndNotesDialogFactory())
			{
				dlg.ShowDialog();
			}
		}

		private void OnShowNotesMenu(object sender, EventArgs e)
		{
			using (var dlg = _historyAndNotesDialogFactory())
			{
				dlg.ShowNotesFirst = true;
				dlg.ShowDialog();
			}
		}

		private void _doChecksAndUpdatesOfAllBooksToolStripMenuItem_Click(object sender, EventArgs e)
		{
			_model.DoUpdatesOfAllBooks();
		}
		private void _doChecksOfAllBooksToolStripMenuItem_Click(object sender, EventArgs e)
		{
			_model.DoChecksOfAllBooks();
		}

		private void _rescueMissingImagesToolStripMenuItem_Click(object sender, EventArgs e)
		{
			using (var dlg = new FolderBrowserDialog())
			{
				dlg.ShowNewFolderButton = false;
				dlg.Description = "Select the folder where replacement images can be found";
				if (DialogResult.OK == dlg.ShowDialog())
				{
					_model.AttemptMissingImageReplacements(dlg.SelectedPath);
				}
			}
		}

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (_newDownloadTimer != null))
			{
				_newDownloadTimer.Stop();
				_newDownloadTimer.Dispose();
			}
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			if (disposing && _downloadedBookCollection != null)
			{
				_downloadedBookCollection.StopWatchingDirectory();
				_downloadedBookCollection.FolderContentChanged -= DownLoadedBooksChanged;
			}
			base.Dispose(disposing);
			_disposed = true;
		}

		internal void MakeBloomPack(bool forReaderTools)
		{
			using (var dlg = new SaveFileDialog())
			{
				dlg.FileName = _model.GetSuggestedBloomPackPath();
				dlg.Filter = "BloomPack|*.BloomPack";
				dlg.RestoreDirectory = true;
				dlg.OverwritePrompt = true;
				if (DialogResult.Cancel == dlg.ShowDialog())
				{
					return;
				}
				_model.MakeBloomPack(dlg.FileName, forReaderTools);
			}
		}
		private void exportToWordOrLibreOfficeToolStripMenuItem_Click(object sender, EventArgs e)
		{
			try
			{
				MessageBox.Show(LocalizationManager.GetString("CollectionTab.BookMenu.ExportDocMessage",
					"Bloom will now open this HTML document in your word processing program (normally Word or LibreOffice). You will be able to work with the text and images of this book, but these programs normally don't do well with preserving the layout, so don't expect much."));
				var destPath = _bookSelection.CurrentSelection.GetPathHtmlFile().Replace(".htm", ".doc");
				_model.ExportDocFormat(destPath);
				PathUtilities.OpenFileInApplication(destPath);
				Analytics.Track("Exported To Doc format");
			}
			catch (IOException error)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error.Message, "Could not export the book");
				Analytics.ReportException(error);
			}
			catch (Exception error)
			{
				Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error, "Could not export the book");
				Analytics.ReportException(error);
			}
		}


		private void makeReaderTemplateBloomPackToolStripMenuItem_Click(object sender, EventArgs e)
		{
			using (var dlg = new MakeReaderTemplateBloomPackDlg())
			{
				dlg.SetLanguage(_model.LanguageName);
				dlg.SetTitles(_model.BookTitles);
				if (dlg.ShowDialog(this) != DialogResult.OK)
					return;
				MakeBloomPack(true);
			}
		}

		private void _menuButton_Click(object sender, EventArgs e)
		{
			_vernacularCollectionMenuStrip.Show(_menuTriangle, new Point(0, 0));
		}

		private class ButtonInfo
		{
			public ButtonInfo(Button button, bool thumbnailRefreshNeeded)
			{
				Button = button;
				ThumbnailRefreshNeeded = thumbnailRefreshNeeded;
			}
			public Button Button { get; set; }
			public bool ThumbnailRefreshNeeded { get; set; }
		}

		private void openCreateCollectionToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var workspaceView = GetWorkspaceView(this, typeof(WorkspaceView));
			if (workspaceView != null)
				workspaceView.OpenCreateLibrary();
		}

		private static WorkspaceView GetWorkspaceView(Control ctrl, Type workspaceViewType)
		{
			while (true)
			{
				var parent = ctrl.Parent;

				if (parent == null)
					return null;

				if (parent.GetType() == workspaceViewType)
					return (WorkspaceView) parent;

				ctrl = parent;
			}
		}
	}
}