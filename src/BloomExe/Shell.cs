using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Bloom.Collection;
using Bloom.Properties;
using Bloom.Workspace;
using Palaso.Extensions;
using Palaso.UI.WindowsForms.PortableSettingsProvider;

namespace Bloom
{
	public partial class Shell : Form
	{
		private readonly CollectionSettings _collectionSettings;
		private readonly LibraryClosing _libraryClosingEvent;
		private readonly WorkspaceView _workspaceView;

		// This is needed because on Linux the ResizeEnd event is firing before the Load event handler is
		// finished, overwriting the saved RestoreBounds before they are applied.
		private bool _finishedLoading;

		public Shell(Func<WorkspaceView> projectViewFactory,
												CollectionSettings collectionSettings,
												BookDownloadStartingEvent bookDownloadStartingEvent,
												LibraryClosing libraryClosingEvent,
												QueueRenameOfCollection queueRenameOfCollection)
		{
			queueRenameOfCollection.Subscribe(newName => _nameToChangeCollectionUponClosing = newName.Trim().SanitizeFilename('-'));
			_collectionSettings = collectionSettings;
			_libraryClosingEvent = libraryClosingEvent;
			InitializeComponent();

			//bring the application to the front (will normally be behind the user's web browser)
			bookDownloadStartingEvent.Subscribe((x) =>
			{
				try
				{
					this.Invoke((Action)this.Activate);
				}
				catch (Exception e)
				{
					Debug.Fail("(Debug Only) Can't bring to front in the current state: " + e.Message);
					//swallow... so we were in some state that we couldn't come to the front... that's ok.
				}
			});


#if DEBUG
			WindowState = FormWindowState.Normal;
			//this.FormBorderStyle = FormBorderStyle.None;  //fullscreen

			Size = new Size(1024,720);
#else
			// We only want this screen size context menu in Debug mode
			ContextMenuStrip = null;
#endif
			_workspaceView = projectViewFactory();
			_workspaceView.CloseCurrentProject += ((x, y) =>
													{
														UserWantsToOpenADifferentProject = true;
														Close();
													});

			_workspaceView.ReopenCurrentProject += ((x, y) =>
			{
				UserWantsToOpeReopenProject = true;
				Close();
			});

			_workspaceView.BackColor =
				System.Drawing.Color.FromArgb(64,64,64);
										_workspaceView.Dock = System.Windows.Forms.DockStyle.Fill;

			this.Controls.Add(this._workspaceView);

			SetWindowText(null);
		}

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);

			// BL-552, BL-779: a bug in Mono requires us to wait to set Icon until handle created.
			this.Icon = global::Bloom.Properties.Resources.Bloom;
		}

		protected override void OnClosing(CancelEventArgs e)
		{
			//get everything saved (under the old collection name, if we are changing the name and restarting)
			_libraryClosingEvent.Raise(null);

			if (!string.IsNullOrEmpty(_nameToChangeCollectionUponClosing) &&
				_nameToChangeCollectionUponClosing != _collectionSettings.CollectionName)
			{
				//Actually restart Bloom with a parameter requesting this name change. It's way more likely to succeed
				//when this run isn't holding onto anything.
				try
				{
					var existingDirectoryPath = Path.GetDirectoryName(_collectionSettings.SettingsFilePath);
					var parentDirectory = Path.GetDirectoryName(existingDirectoryPath);
					var newDirectoryPath = Path.Combine(parentDirectory, _nameToChangeCollectionUponClosing);

					Process.Start(Application.ExecutablePath,
						string.Format("--rename \"{0}\" \"{1}\" ", existingDirectoryPath, newDirectoryPath));

					//give some time for that process.start to finish staring the new instance, which will see
					//we have a mutex and wait for us to die, then see the --rename, and do its work.

					Thread.Sleep(2000);
					Environment.Exit(-1); //Force termination of the current process.
				}
				catch (Exception error)
				{
					Palaso.Reporting.ErrorReport.NotifyUserOfProblem(error,
						"Sorry, Bloom failed to even prepare for the rename of the project to '{0}'", _nameToChangeCollectionUponClosing);
				}
			}

			base.OnClosing(e);
		}

		public void SetWindowText(string bookName)
		{
			string formattedText = string.Format("{0} - Bloom {1} Built on {2}", _workspaceView.Text, GetShortVersionInfo(), GetBuiltOnDate());
			if (bookName != null)
			{
				formattedText = string.Format("{0} - {1}", bookName, formattedText);
			}
			if(_collectionSettings.IsSourceCollection)
			{
				formattedText += " SOURCE COLLECTION";
			}
			Text = formattedText;
		}

		public static string GetBuiltOnDate()
		{
			var asm = Assembly.GetExecutingAssembly();
			var ver = asm.GetName().Version;
			var file = asm.CodeBase.Replace("file://", string.Empty);
			if (Palaso.PlatformUtilities.Platform.IsWindows)
				file = file.TrimStart('/');
			var fi = new FileInfo(file);

			return string.Format("{0}",fi.CreationTimeUtc.ToString("dd-MMM-yyyy"));
		}

		public static string GetShortVersionInfo()
		{
			var asm = Assembly.GetExecutingAssembly();
			var ver = asm.GetName().Version;

			return string.Format("{0}.{1}.{2}", ver.Major, ver.Minor, ver.Build);
		}

		public bool UserWantsToOpenADifferentProject { get; set; }

		public bool UserWantsToOpeReopenProject;

		/// <summary>
		/// used when the user does an in-app installer download; after we close down, Program will read this and return control to Sparkle
		/// </summary>
		public bool QuitForVersionUpdate;

		public bool QuitForSystemShutdown;

		private string _nameToChangeCollectionUponClosing;


		private void Shell_Activated(object sender, EventArgs e)
		{

		}

		private void Shell_Deactivate(object sender, EventArgs e)
		{
			Debug.WriteLine("Shell Deactivated");
		}

		private void On800x600Click(object sender, EventArgs e)
		{
			Size = new Size(800,600);
		}

		private void On1024x600Click(object sender, EventArgs e)
		{
			Size = new Size(1024, 600);
		}

		private void On1024x768(object sender, EventArgs e)
		{
			Size = new Size(1024, 768);
		}

		private void On1024x586(object sender, EventArgs e)
		{
			Size = new Size(1024, 586);
		}

		/// <summary>
		/// we let the Program call this after it closes the splash screen
		/// </summary>
		public void ReallyComeToFront()
		{
			//try really hard to become top most. See http://stackoverflow.com/questions/5282588/how-can-i-bring-my-application-window-to-the-front
			TopMost = true;
			Focus();
			BringToFront();
			TopMost = false;

			_finishedLoading = true;
		}

		private void Shell_Load(object sender, EventArgs e)
		{
			//Handle window sizing/location. Normally, we just Maximize the window.
			//The exceptions to this are if we are in a DEBUG build or the settings have a MaximizeWindow=='False", which at this time
			//must be done by hand (no user UI is provided).
			try
			{
				SuspendLayout();

				if(Settings.Default.WindowSizeAndLocation == null)
				{
					StartPosition = FormStartPosition.WindowsDefaultLocation;
					WindowState = FormWindowState.Maximized;
					Settings.Default.WindowSizeAndLocation = FormSettings.Create(this);
					Settings.Default.Save();
				}

				// This feature is not yet a normal part of Bloom, since we think just maximizing is more rice-farmer-friendly.
				// However, we added the ability to remember this stuff at the request of the person making videos, who needs
				// Bloom to open in the same place / size each time.
				if (Settings.Default.MaximizeWindow == false)
				{
					Settings.Default.WindowSizeAndLocation.InitializeForm(this);
				}
				else
				{
					// BL-1036: save and restore un-maximized settings
					var savedBounds = Settings.Default.RestoreBounds;
					if ((savedBounds.Width > 200) && (savedBounds.Height > 200) && (IsOnScreen(savedBounds)))
					{
						StartPosition = FormStartPosition.Manual;
						WindowState = FormWindowState.Normal;
						Bounds = savedBounds;
					}
					else
					{
						StartPosition = FormStartPosition.CenterScreen;
					}

					WindowState = FormWindowState.Maximized;
				}
			}
			catch (Exception error)
			{
				Debug.Fail(error.Message);
				
// ReSharper disable HeuristicUnreachableCode
				//Not worth bothering the user. Just reset the values to something reasonable.
				StartPosition = FormStartPosition.WindowsDefaultLocation;
				WindowState = FormWindowState.Maximized;
// ReSharper restore HeuristicUnreachableCode
			}
			finally
			{
				ResumeLayout();
			}
		}

		private void Shell_ResizeEnd(object sender, EventArgs e)
		{
			// BL-1036: save and restore un-maximized settings
			if (!_finishedLoading) return;
			if (WindowState != FormWindowState.Normal) return;

			Settings.Default.RestoreBounds = new Rectangle(Left, Top, Width, Height);
			Settings.Default.Save();
		}

		/// <summary>
		/// Is a significant (100 x 100) portion of the form on-screen?
		/// </summary>
		/// <returns></returns>
		private static bool IsOnScreen(Rectangle rect)
		{
			var screens = Screen.AllScreens;
			var formTopLeft = new Rectangle(rect.Left, rect.Top, 100, 100);

			return screens.Any(screen => screen.WorkingArea.Contains(formTopLeft));
		}
	}
}
