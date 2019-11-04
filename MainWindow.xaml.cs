﻿using NLog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using JetBrains.Annotations;
using TVRename.Ipc;
using Directory = Alphaleonis.Win32.Filesystem.Directory;
using File = Alphaleonis.Win32.Filesystem.File;
using FileInfo = Alphaleonis.Win32.Filesystem.FileInfo;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TVRename.View.Supporting;
using TVRename.ViewModel;
using MessageBox = System.Windows.MessageBox;
using DataGrid = System.Windows.Controls.DataGrid;

namespace TVRename
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        MyShowsViewModel viewMyShowsViewModel;
        MainWindowViewModel mainViewModel;
        WhenToWatchViewModel wtwViewModel;

        public MainWindow(TVDoc doc, [NotNull] SplashViewModel splash,Dispatcher dispy, bool showUi)
        {
            mDoc = doc;

            busy = 0;
            mLastEpClicked = null;
            mLastFolderClicked = null;
            mLastSeasonClicked = null;
            mLastShowsClicked = null;
            mLastActionsClicked = null;

            mInternalChange = 0;
            mFoldersToOpen = new List<string>();

            internalCheckChange = false;

            InitializeComponent();

            SetupIpc();

            try
            {
                bool layoutLoadSuccess = LoadLayoutXml();
                if (!layoutLoadSuccess)
                {
                    Logger.Info("Error loading layout XML, but no error raised");
                }
            }
            catch (Exception e)
            {
                // silently fail, doesn't matter too much
                Logger.Info(e, "Error loading layout XML");
            }

            //lvWhenToWatch.ListViewItemSorter = new DateSorterWtw(0);

            if (mDoc.Args.Hide || !showUi)
            {
                WindowState = WindowState.Minimized;
                Visibility = Visibility.Hidden;
                Hide();
            }

            //tmrPeriodicScan.Enabled = false;

            UpdateSplashStatus(splash,dispy, "Filling Shows");
            FillMyShows();
            UpdateSearchButtons();
            //ClearInfoWindows();
            UpdateSplashPercent(splash, dispy, 10);
            UpdateSplashStatus(splash, dispy, "Updating WTW");
            mDoc.DoWhenToWatch(true, true, WindowState == WindowState.Minimized);
            UpdateSplashPercent(splash, dispy, 40);
            //FillWhenToWatchList();
            UpdateSplashPercent(splash, dispy, 60);
            UpdateSplashStatus(splash, dispy, "Write Upcoming");
            mDoc.WriteUpcoming();
            UpdateSplashStatus(splash, dispy, "Write Recent");
            mDoc.WriteRecent();
            UpdateSplashStatus(splash, dispy, "Setting Notifications");
            ShowHideNotificationIcon();

            viewMyShowsViewModel = new MyShowsViewModel(mDoc.Library);
            wtwViewModel = new WhenToWatchViewModel(mDoc.Library);
            mainViewModel = new MainWindowViewModel() {Airing = "Next", Downloading = "Idle", Status = 50};

            viewMyShowsViewModel.Filter = TVSettings.Instance.Filter;

            this.tbMyShows.DataContext = viewMyShowsViewModel;
            this.sbBottomStatusBar.DataContext = mainViewModel;
            this.tbWTW.DataContext = wtwViewModel;
            //this.MyShowTree.DataContext = viewMyShowsViewModel.VisibleShows;


            int t = TVSettings.Instance.StartupTab;
            if (t < tabControl1.Items.Count)
            {
                tabControl1.SelectedIndex = TVSettings.Instance.StartupTab;
            }

            tabControl1_SelectedIndexChanged(null, null);
            UpdateSplashStatus(splash, dispy, "Creating Monitors");

            mAutoFolderMonitor = new AutoFolderMonitor(mDoc, this);

            //TODO tmrPeriodicScan.Interval = TVSettings.Instance.PeriodicCheckPeriod();

            UpdateSplashStatus(splash, dispy, "Starting Monitor");
            if (TVSettings.Instance.MonitorFolders)
            {
                mAutoFolderMonitor.Start();
            }

            //TODO tmrPeriodicScan.Enabled = TVSettings.Instance.RunPeriodicCheck();

            UpdateSplashStatus(splash, dispy, "Running autoscan");



        }

        public static string EXPLORE_PROXY => "http://www.tvrename.com/EXPLOREPROXY";
        public static string WATCH_PROXY => "http://www.tvrename.com/WATCHPROXY";

        public void Invoke(object afmDoAll)
        {
            throw new System.NotImplementedException();
        }

        // right click commands
        public enum RightClickCommands
        {
            kEpisodeGuideForShow = 1,
            kVisitTvdbEpisode,
            kVisitTvdbSeason,
            kVisitTvdbSeries,
            kScanSpecificSeries,
            kWhenToWatchSeries,
            kForceRefreshSeries,
            kBtSearchFor,
            kActionIgnore,
            kActionBrowseForFile,
            kActionAction,
            kActionDelete,
            kActionIgnoreSeason,
            kEditShow,
            kEditSeason,
            kDeleteShow,
            kUpdateImages,
            kActionRevert,
            kWatchBase = 1000,
            kOpenFolderBase = 2000,
            kSearchForBase = 3000
        }

        #region Delegates

        public delegate void AutoFolderMonitorDelegate();

        #endregion

        private int busy;
        private TVDoc mDoc;
        private bool internalCheckChange;
        private int lastDlRemaining;

        public AutoFolderMonitorDelegate AfmFullScan;
        public AutoFolderMonitorDelegate AfmRecentScan;
        public AutoFolderMonitorDelegate AfmQuickScan;
        public AutoFolderMonitorDelegate AfmDoAll;

        private List<string> mFoldersToOpen;
        private int mInternalChange;
        private List<FileInfo> mLastFl;
        private Point mLastNonMaximizedLocation;
        private Size mLastNonMaximizedSize;
        private readonly AutoFolderMonitor mAutoFolderMonitor;
        private bool treeExpandCollapseToggle = true;

        private ItemList mLastActionsClicked;
        private ProcessedEpisode mLastEpClicked;
        private readonly string mLastFolderClicked;
        private Season mLastSeasonClicked;
        private List<ShowItem> mLastShowsClicked;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();


        private static void UpdateSplashStatus([NotNull] SplashViewModel splashScreen, Dispatcher dispy, string text)
        {
            Logger.Info($"Splash Screen Updated with: {text}");
            dispy.Invoke(()=>splashScreen.Status = text);
        }

        private static void UpdateSplashPercent([NotNull] SplashViewModel splashScreen, Dispatcher dispy, int num)
        {
            dispy.Invoke(() => splashScreen.Progress = num);
        }

        private static int BgdlLongInterval() => 1000 * 60 * 60; // one hour

        private void MoreBusy() => Interlocked.Increment(ref busy);

        private void LessBusy() => Interlocked.Decrement(ref busy);

        private void SetupIpc()
        {
            AfmFullScan += Scan;
            AfmQuickScan += QuickScan;
            AfmRecentScan += RecentScan;
            AfmDoAll += ProcessAll;
        }

        private void ProcessArgs()
        {
            // TODO: Unify command line handling between here and in Program.cs

            if (mDoc.Args.Scan)
            {
                UiScan(null, true, TVSettings.ScanType.Full);
            }

            if (mDoc.Args.QuickScan)
            {
                UiScan(null, true, TVSettings.ScanType.Quick);
            }

            if (mDoc.Args.RecentScan)
            {
                UiScan(null, true, TVSettings.ScanType.Recent);
            }

            if (mDoc.Args.DoAll)
            {
                ProcessAll();
            }

            if (mDoc.Args.Quit || mDoc.Args.Hide)
            {
                Close();
            }
        }

        private void UpdateSearchButtons()
        {
            string name = TVDoc.GetSearchers().Name(TVSettings.Instance.TheSearchers.CurrentSearchNum());

            //bnWTWBTSearch.Enabled = !string.IsNullOrWhiteSpace(name);
            //bnActionBTSearch.Enabled = !string.IsNullOrWhiteSpace(name);

            //bnWTWBTSearch.Text = UseCustom(lvWhenToWatch) ? "Search" : name;
            //bnActionBTSearch.Text = UseCustom(lvAction) ? "Search" : name;

            //FillEpGuideHtml();
        }

        private void visitWebsiteToolStripMenuItem_Click(object sender, EventArgs eventArgs) =>
            Helpers.SysOpen("http://tvrename.com");

        private void exitToolStripMenuItem_Click(object sender, EventArgs e) => Close();

/*        private static bool UseCustom([NotNull] ListView view)
        {
            foreach (ListViewItem lvi in view.SelectedItems)
            {
                if (!(lvi.Tag is ProcessedEpisode pe))
                {
                    continue;
                }

                if (!pe.Show.UseCustomSearchUrl)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(pe.Show.CustomSearchUrl))
                {
                    continue;
                }

                return true;
            }

            return false;
        }*/

        private void UI_Load(object sender, EventArgs e)
        {
            ShowInTaskbar = TVSettings.Instance.ShowInTaskbar && !mDoc.Args.Hide;

/*
            foreach (TabPage tp in tabControl1.TabPages) // grr! TODO: why does it go white?
            {
                tp.BackColor = SystemColors.Control;
            }

            // MAH: Create a "Clear" button in the Filter Text Box
            Button filterButton = new Button
            {
                Size = new Size(16, 16),
                Cursor = Cursors.Default,
                Image = Properties.Resources.DeleteSmall,
                Name = "Clear"
            };

            filterButton.Location = new Point(filterTextBox.ClientSize.Width - filterButton.Width,
                ((filterTextBox.ClientSize.Height - 16) / 2) + 1);

            filterButton.Click += filterButton_Click;
            filterTextBox.Controls.Add(filterButton);
            // Send EM_SETMARGINS to prevent text from disappearing underneath the button
            NativeMethods.SendMessage(filterTextBox.Handle, 0xd3, (IntPtr)2, (IntPtr)(filterButton.Width << 16));

            betaToolsToolStripMenuItem.Visible = TVSettings.Instance.IncludeBetaUpdates();

*/
            Show();
            UI_LocationChanged(null, null);
            UI_SizeChanged(null, null);

/*            ToolTip tt = new ToolTip();
            tt.SetToolTip(btnActionQuickScan,
                "Scan shows with missing recent aired episodes and and shows that match files in the search folders");

            tt.SetToolTip(bnActionRecentCheck, "Scan shows with recent aired episodes");
            tt.SetToolTip(bnActionCheck, "Scan all shows");

            backgroundDownloadToolStripMenuItem.Checked = TVSettings.Instance.BGDownload;
            offlineOperationToolStripMenuItem.Checked = TVSettings.Instance.OfflineMode;
            BGDownloadTimer.Interval = 10000; // first time
            if (TVSettings.Instance.BGDownload)
            {
                BGDownloadTimer.Start();
            }

            UpdateTimer.Start();

            quickTimer.Start();*/

            if (TVSettings.Instance.RunOnStartUp())
            {
                RunAutoScan("Startup Scan");
            }
        }

        // MAH: Added in support of the Filter TextBox Button
        private void filterButton_Click(object sender, EventArgs e) => filterTextBox.Clear();

        private DataGrid ListViewByName([NotNull] string name)
        {
            switch (name)
            {
                case "WhenToWatch":
                    return lvWhenToWatch;
                case "AllInOne":
                    return lvAction;
                default:
                    throw new ArgumentException("Inappropriate ListViewParameter " + name);
            }
        }

        private void flushCacheToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (busy != 0)
            {
                MessageBox.Show("Can't refresh until background download is complete");
                return;
            }

            MessageBoxResult res = MessageBox.Show(
                "Are you sure you want to remove all " +
                "locally stored TheTVDB information?  This information will have to be downloaded again.  You " +
                "can force the refresh of a single show by holding down the \"Control\" key while clicking on " +
                "the \"Refresh\" button in the \"My Shows\" tab.",
                "Force Refresh All", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (res == MessageBoxResult.Yes)
            {
                TheTVDB.Instance.ForgetEverything();
                FillMyShows();
                //FillEpGuideHtml();
                //FillWhenToWatchList();
                BGDownloadTimer_QuickFire();
            }
        }

        private bool LoadWidths([NotNull] XElement xml)
        {
            string forwho = xml.Attribute("For")?.Value;

            if (forwho is null)
            {
                return false;
            }

/*
            ListView lv = ListViewByName(forwho);
            if (lv is null)
            {
                return true;
            }

            int c = 0;
            foreach (XElement w in xml.Descendants("Width"))
            {
                if (c >= lv.Columns.Count)
                {
                    return false;
                }

                lv.Columns[c++].Width = XmlConvert.ToInt32(w.Value);
            }
*/

            return true;
        }

        private bool LoadLayoutXml()
        {
            if (mDoc.Args.Hide)
            {
                return true;
            }

            bool ok = true;

            string fn = PathManager.UILayoutFile.FullName;
            if (!File.Exists(fn))
            {
                return true;
            }

            XElement x = XElement.Load(fn);

            if (x.Name.LocalName != "TVRename")
            {
                return false;
            }

            if (x.Attribute("Version")?.Value != "2.1")
            {
                return false;
            }

            if (!x.Descendants("Layout").Any())
            {
                return false;
            }

            SetWindowSize(x.Descendants("Layout").Descendants("Window").First());

            foreach (XElement widthXmlElement in x.Descendants("Layout").Descendants("ColumnWidths"))
            {
                ok = LoadWidths(widthXmlElement) && ok;
            }

            SetSplitter(x.Descendants("Layout").Descendants("Splitter").First());

            return ok;
        }

        private void SetSplitter([NotNull] XElement x)
        {
//            splitContainer1.SplitterDistance = int.Parse(x.Attribute("Distance")?.Value ?? "100");
//            splitContainer1.Panel2Collapsed = bool.Parse(x.Attribute("HTMLCollapsed")?.Value ?? "false");
//            if (splitContainer1.Panel2Collapsed)
//            {
//                bnHideHTMLPanel.ImageKey = "FillLeft.bmp";
//            }
        }

        private void SetWindowSize([NotNull] XElement x)
        {
            SetSize(x.Descendants("Size").First());
            SetLocation(x.Descendants("Location").First());

            WindowState = (x.ExtractBool("Maximized", false))
                ? WindowState.Maximized
                : WindowState.Normal;
        }

        private void SetLocation([NotNull] XElement x)
        {
            XAttribute valueX = x.Attribute("X");
            XAttribute valueY = x.Attribute("Y");

            if (valueX is null)
            {
                Logger.Error($"Missing X from {x}");
            }
            if (valueY == null)
            {
                Logger.Error($"Missing Y from {x}");
            }

            int xloc = (valueX == null) ? 100 : int.Parse(valueX.Value);
            int yloc = (valueY is null) ? 100 : int.Parse(valueY.Value);

            //Position = new Point(xloc, yloc);
        }

        private void SetSize([NotNull] XElement x)
        {
            XAttribute valueX = x.Attribute("Width");
            XAttribute valueY = x.Attribute("Height");

            if (valueX is null)
            {
                Logger.Error($"Missing Width from {x}");
            }
            if (valueY is null)
            {
                Logger.Error($"Missing Height from {x}");
            }

            int xsize = (valueX is null) ? 100 : int.Parse(valueX.Value);
            int ysize = (valueY is null) ? 100 : int.Parse(valueY.Value);

            //Size = new Size(xsize, ysize);
        }

        private bool SaveLayoutXml()
        {
            if (mDoc.Args.Hide)
            {
                return true;
            }

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                NewLineOnAttributes = true
            };

            using (XmlWriter writer = XmlWriter.Create(PathManager.UILayoutFile.FullName, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("TVRename");
                writer.WriteAttributeToXml("Version", "2.1");
                writer.WriteStartElement("Layout");
                writer.WriteStartElement("Window");

                writer.WriteStartElement("Size");
                writer.WriteAttributeToXml("Width", mLastNonMaximizedSize.Width);
                writer.WriteAttributeToXml("Height", mLastNonMaximizedSize.Height);
                writer.WriteEndElement(); // size

                writer.WriteStartElement("Location");
                writer.WriteAttributeToXml("X", mLastNonMaximizedLocation.X);
                writer.WriteAttributeToXml("Y", mLastNonMaximizedLocation.Y);
                writer.WriteEndElement(); // Location

                writer.WriteElement("Maximized", WindowState == WindowState.Maximized);

                writer.WriteEndElement(); // window

                WriteColWidthsXml("WhenToWatch", writer);
                WriteColWidthsXml("AllInOne", writer);

                writer.WriteStartElement("Splitter");
                //writer.WriteAttributeToXml("Distance", splitContainer1.SplitterDistance);
                //writer.WriteAttributeToXml("HTMLCollapsed", splitContainer1.Panel2Collapsed);
                writer.WriteEndElement(); // splitter

                writer.WriteEndElement(); // Layout
                writer.WriteEndElement(); // tvrename
                writer.WriteEndDocument();
            }

            return true;
        }

        private void WriteColWidthsXml([NotNull] string thingName, XmlWriter writer)
        {
            DataGrid lv = ListViewByName(thingName);
            if (lv is null)
            {
                return;
            }

            writer.WriteStartElement("ColumnWidths");
            writer.WriteAttributeToXml("For", thingName);
            foreach (DataGridColumn lvc in lv.Columns)
            {
                writer.WriteElement("Width", lvc.Width.Value);
            }

            // ReSharper disable once CommentTypo
            writer.WriteEndElement(); // columnwidths
        }

        private void UI_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                if (mDoc.Dirty())
                {
                    MessageBoxResult res = MessageBox.Show(
                        "Your changes have not been saved.  Do you wish to save before quitting?", "Unsaved data",
                        MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

                    switch (res)
                    {
                        case MessageBoxResult.Yes:
                            mDoc.WriteXMLSettings();
                            break;
                        case MessageBoxResult.Cancel:
                            e.Cancel = true;
                            break;
                        case MessageBoxResult.No:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                if (!e.Cancel)
                {
                    SaveLayoutXml();
                    mDoc.TidyTvdb();
                    mDoc.Closing();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message + "\r\n\r\n" + ex.StackTrace, "Form Closing Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [NotNull]
        //private ContextMenuStrip BuildSearchMenu()
        //{
/*            menuSearchSites.Items.Clear();
            for (int i = 0; i < TVDoc.GetSearchers().Count(); i++)
            {
                string name = TVDoc.GetSearchers().Name(i);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    ToolStripMenuItem tsi = new ToolStripMenuItem(name) { Tag = i };
                    menuSearchSites.Items.Add(tsi);
                }
            }

            return menuSearchSites;*/
        //}

        private void ChooseSiteMenu(int n)
        {
/*            ContextMenuStrip sm = BuildSearchMenu();
            if (n == 1)
            {
                sm.Show(bnWTWChooseSite, new Point(0, 0));
            }
            else if (n == 0)
            {
                sm.Show(bnActionWhichSearch, new Point(0, 0));
            }
            else
            {
                throw new ArgumentOutOfRangeException();
            }*/
        }

       // private void bnWTWChooseSite_Click(object sender, EventArgs e) => ChooseSiteMenu(1);

        private void FillMyShows()
        {
            //MyShowTree.Sort();

            /*Season currentSeas = TreeViewItemToSeason((TreeViewItem)MyShowTree.SelectedItem );
            ShowItem currentSi = TreeViewItemToShowItem((TreeViewItem)MyShowTree.SelectedItem);

            List<ShowItem> expanded =  
                MyShowTree.Items.Cast<TreeViewItem>()
                    .Where(n => n.IsExpanded)
                    .Select(TreeViewItemToShowItem).ToList();

            //MyShowTree.BeginUpdate();

            MyShowTree.Items.Clear();
            List<ShowItem> sil = mDoc.Library.GetShowItems();
            lock (TheTVDB.SERIES_LOCK)
            {
                sil.Sort((a, b) =>
                {
                    SeriesInfo serA = TheTVDB.Instance.GetSeries(a.TvdbCode);
                    SeriesInfo serB = TheTVDB.Instance.GetSeries(b.TvdbCode);
                    return string.Compare(GenerateShowUIName(serA, a), GenerateShowUIName(serB, b), StringComparison.OrdinalIgnoreCase);
                });
            }

            ShowFilter filter = TVSettings.Instance.Filter;
            foreach (ShowItem si in sil)
            {
                if (filter.Filter(si)
                    & (string.IsNullOrEmpty(filterTextBox.Text) || si.NameMatchFilters(filterTextBox.Text)))
                {
                    TreeViewItem tvn = AddShowItemToTree(si);
                    if (expanded.Contains(si))
                    {
                        tvn.IsExpanded = true;
                    }
                }
            }

            foreach (ShowItem si in expanded)
            {
                foreach (TreeViewItem n in MyShowTree.Items)
                {
                    if (TreeViewItemToShowItem(n) == si)
                    {
                        n.IsExpanded = true;
                    }
                }
            }

            if (currentSeas != null)
            {
                SelectSeason(currentSeas);
            }
            else if (currentSi != null)
            {
                SelectShow(currentSi);
            }

            //MyShowTree.EndUpdate();
            */
        }

        [NotNull]
        private static string QuickStartGuide() => "https://www.tvrename.com/manual/quickstart/";

        private void ShowQuickStartGuide()
        {
            tabControl1.SelectedItem =tbMyShows;
            webInformation.Navigate(QuickStartGuide());
            webImages.Navigate(QuickStartGuide());
        }




        private ShowItem TreeViewItemToShowItem([CanBeNull] TreeViewItem n)
        {
            if (n is null)
            {
                return null;
            }

            if (n.Tag is ShowItem si)
            {
                return si;
            }

            if (n.Tag is ProcessedEpisode pe)
            {
                return pe.Show;
            }

            if (n.Tag is Season seas)
            {
                if (seas.Episodes.Count == 0)
                {
                    return null;
                }

                return mDoc.Library.ShowItem(seas.TheSeries.TvdbCode);
            }

            return null;
        }


        [CanBeNull]
        private static Season TreeViewItemToSeason([CanBeNull] TreeViewItem n)
        {
            Season seas = n?.Tag as Season;
            return seas;
        }


        private static void TvdbFor([CanBeNull] ProcessedEpisode e)
        {
            if (e != null)
            {
                Helpers.SysOpen(TheTVDB.Instance.WebsiteUrl(e.Show.TvdbCode, e.SeasonId, false));
            }
        }

        private static void TvdbFor([CanBeNull] Season seas)
        {
            if (seas != null)
            {
                Helpers.SysOpen(TheTVDB.Instance.WebsiteUrl(seas.TheSeries.TvdbCode, -1, false));
            }
        }

        private static void TvdbFor([CanBeNull] ShowItem si)
        {
            if (si != null)
            {
                Helpers.SysOpen(TheTVDB.Instance.WebsiteUrl(si.TvdbCode, -1, false));
            }
        }

/*
        private void menuSearchSites_ItemClicked(object sender, [NotNull] ToolStripItemClickedEventArgs e)
        {
            mDoc.SetSearcher((int)e.ClickedItem.Tag);
            UpdateSearchButtons();
        }
*/

        private void bnWhenToWatchCheck_Click(object sender, EventArgs e) => RefreshWTW(true, false);

        /*
                private void lvWhenToWatch_ColumnClick(object sender, [NotNull] ColumnClickEventArgs e)
                {
                    int col = e.Column;
                    // 3 - 6 = do date sort on 3
                    // 1 or 2 = number sort
                    // all others, text sort

                    lvWhenToWatch.ShowGroups = false;

                    switch (col)
                    {
                        case 3:
                        case 4:
                        case 5:
                        case 6:
                            lvWhenToWatch.ShowGroups = true;
                            lvWhenToWatch.ListViewItemSorter = new DateSorterWtw(col);
                            break;
                        case 1:
                        case 2:
                            lvWhenToWatch.ListViewItemSorter = new NumberAsTextSorter(col);
                            break;
                        default:
                            lvWhenToWatch.ListViewItemSorter = new TextSorter(col);
                            break;
                    }

                    lvWhenToWatch.Sort();
                    lvWhenToWatch.Refresh();
                }
        */

        /*
                private void lvWhenToWatch_Click(object sender, EventArgs e)
                {
                    UpdateSearchButtons();

                    if (lvWhenToWatch.SelectedIndices.Count == 0)
                    {
                        txtWhenToWatchSynopsis.Text = "";
                        return;
                    }

                    int n = lvWhenToWatch.SelectedIndices[0];

                    ProcessedEpisode ei = (ProcessedEpisode)lvWhenToWatch.Items[n].Tag;

                    if (TVSettings.Instance.HideWtWSpoilers &&
                        (ei.HowLong() != "Aired" || lvWhenToWatch.Items[n].ImageIndex == 1))
                    {
                        txtWhenToWatchSynopsis.Text = "[Spoilers Hidden]";
                    }
                    else
                    {
                        txtWhenToWatchSynopsis.Text = ei.Overview;
                    }

                    mInternalChange++;
                    DateTime? dt = ei.GetAirDateDt(true);
                    if (dt != null)
                    {
                        calCalendar.SelectionStart = (DateTime)dt;
                        calCalendar.SelectionEnd = (DateTime)dt;
                    }

                    mInternalChange--;

                    if (TVSettings.Instance.AutoSelectShowInMyShows)
                    {
                        GotoEpguideFor(ei, false);
                    }
                }
        */

        /*
                private void lvWhenToWatch_DoubleClick(object sender, EventArgs e)
                {
                    if (lvWhenToWatch.SelectedItems.Count == 0)
                    {
                        return;
                    }

                    ProcessedEpisode ei = (ProcessedEpisode)lvWhenToWatch.SelectedItems[0].Tag;
                    List<FileInfo> fl = FinderHelper.FindEpOnDisk(null, ei);
                    if (fl.Count > 0)
                    {
                        Helpers.SysOpen(fl[0].FullName);
                        return;
                    }

                    // Don't have the episode.  Scan or search?

                    switch (TVSettings.Instance.WTWDoubleClick)
                    {
                        default:
                        case TVSettings.WTWDoubleClickAction.Search:
                            bnWTWBTSearch_Click(null, null);
                            break;
                        case TVSettings.WTWDoubleClickAction.Scan:
                            UiScan(new List<ShowItem> { ei.Show }, false, TVSettings.ScanType.SingleShow);
                            tabControl1.SelectTab(tbAllInOne);
                            break;
                    }
                }
        */

        /*
                private void calCalendar_DateSelected(object sender, DateRangeEventArgs e)
                {
                    if (mInternalChange != 0)
                    {
                        return;
                    }

                    DateTime dt = calCalendar.SelectionStart;
                    bool first = true;

                    foreach (ListViewItem lvi in lvWhenToWatch.Items)
                    {
                        lvi.Selected = false;

                        ProcessedEpisode ei = (ProcessedEpisode)lvi.Tag;
                        DateTime? dt2 = ei.GetAirDateDt(true);
                        if (dt2 != null)
                        {
                            double h = dt2.Value.Subtract(dt).TotalHours;
                            if (h >= 0 && h < 24.0)
                            {
                                lvi.Selected = true;
                                if (first)
                                {
                                    lvi.EnsureVisible();
                                    first = false;
                                }
                            }
                        }
                    }

                    lvWhenToWatch.Focus();
                }
        */

        // ReSharper disable once InconsistentNaming
        private void RefreshWTW(bool doDownloads, bool unattended)
        {
            if (doDownloads)
            {
                if (!mDoc.DoDownloadsFG(unattended, WindowState == WindowState.Minimized))
                {
                    return;
                }
            }

            mInternalChange++;
            mDoc.DoWhenToWatch(true, unattended, WindowState == WindowState.Minimized);
            FillMyShows();
            //FillWhenToWatchList();
            mInternalChange--;

            mDoc.WriteUpcoming();
            mDoc.WriteRecent();
        }

        private void refreshWTWTimer_Tick(object sender, EventArgs e)
        {
            if (busy == 0)
            {
                RefreshWTW(false, true);
            }
        }

        // ReSharper disable once InconsistentNaming
        private void UpdateToolstripWTW()
        {
            // update toolstrip text too
            List<ProcessedEpisode> next1 = mDoc.Library.NextNShows(1, 0, 36500);

            if (next1.Count >= 1)
            {
                ProcessedEpisode ei = next1[0];
                mainViewModel.Airing = CustomEpisodeName.NameForNoExt(ei, CustomEpisodeName.OldNStyle(1)) + ", " + ei.HowLong + " (" + ei.DayOfWeek + ", " + ei.TimeOfDay + ")";
            }
            else
            {
                mainViewModel.Airing = "---";
            }
        }

        private void bnWTWBTSearch_Click(object sender, EventArgs e)
        {
            foreach (object lvi in lvWhenToWatch.SelectedItems)
            {
                //TVDoc.SearchForEpisode((ProcessedEpisode)lvi.Tag);
            }
        }

/*
        private void NavigateTo(object sender, [NotNull] WebBrowserNavigatingEventArgs e)
        {
            if (e.Url is null)
            {
                return;
            }

            string url = e.Url.AbsoluteUri;

            if (string.Compare(url, "about:blank", StringComparison.Ordinal) == 0)
            {
                return; // don't intercept about:blank
            }

            if (url == QuickStartGuide())
            {
                return; // let the quick-start guide be shown
            }

            if (url.Contains(@"ieframe.dll"))
            {
                url = e.Url.Fragment.Substring(1);
            }

            if (url.StartsWith(EXPLORE_PROXY, StringComparison.InvariantCultureIgnoreCase))
            {
                e.Cancel = true;
                string path = System.Net.WebUtility.UrlDecode(url.Substring(EXPLORE_PROXY.Length).Replace('/', '\\'));
                Helpers.SysOpen("explorer", $"/select, \"{path}\"");
                return;
            }

            if (url.StartsWith(WATCH_PROXY, StringComparison.InvariantCultureIgnoreCase))
            {
                e.Cancel = true;
                string fileName = System.Net.WebUtility.UrlDecode(url.Substring(WATCH_PROXY.Length)).Replace('/', '\\');
                Helpers.SysOpen(fileName);
                return;
            }

            if (string.Compare(url.Substring(0, 7), "http://", StringComparison.Ordinal) == 0 ||
                string.Compare(url.Substring(0, 7), "file://", StringComparison.Ordinal) == 0 ||
                string.Compare(url.Substring(0, 8), "https://", StringComparison.Ordinal) == 0)
            {
                e.Cancel = true;
                Helpers.SysOpen(e.Url.AbsoluteUri);
            }
        }
*/
//
//        private void notifyIcon1_Click(object sender, MouseEventArgs e)
//        {
//            // double-click of notification icon causes a click then double-click event, 
//            // so we need to do a timeout before showing the single click's popup
//            //tmrShowUpcomingPopup.Start();
//        }

        private void tmrShowUpcomingPopup_Tick(object sender, EventArgs e)
        {
            //tmrShowUpcomingPopup.Stop();
            //UpcomingPopup up = new UpcomingPopup(mDoc);
            //up.Show();
        }

        public void FocusWindow()
        {
            if (!TVSettings.Instance.ShowInTaskbar)
            {
                Show();
            }

            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Activate();
        }

        public void Scan()
        {
            UiScan(null, true, TVSettings.ScanType.Full);
        }

//        private void notifyIcon1_DoubleClick(object sender, MouseEventArgs e)
//        {
//            //tmrShowUpcomingPopup.Stop();
//            FocusWindow();
//        }

        private void buyMeADrinkToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //BuyMeADrink bmad = new BuyMeADrink();
            //bmad.ShowDialog();
        }

        public void GotoEpguideFor(ShowItem si, bool changeTab)
        {
            if (changeTab)
            {
                tabControl1.SelectedItem = tbMyShows;
            }

            //FillEpGuideHtml(si, -1);
        }

        public void GotoEpguideFor([NotNull] ProcessedEpisode ep, bool changeTab)
        {
            if (changeTab)
            {
                tabControl1.SelectedItem = tbMyShows;
            }

            SelectSeason(ep.AppropriateSeason);
        }

        private void RightClickOnMyShows(ShowItem si, Point pt)
        {
            mLastShowsClicked = new List<ShowItem> { si };
            mLastEpClicked = null;
            mLastSeasonClicked = null;
            mLastActionsClicked = null;
            //BuildRightClickMenu(pt);
        }

        private void RightClickOnMyShows([NotNull] Season seas, Point pt)
        {
            mLastShowsClicked = new List<ShowItem> { mDoc.Library.ShowItem(seas.TheSeries.TvdbCode) };
            mLastEpClicked = null;
            mLastSeasonClicked = seas;
            mLastActionsClicked = null;
            //BuildRightClickMenu(pt);
        }

        private void WtwRightClickOnShow([NotNull] List<ProcessedEpisode> eps, Point pt)
        {
            if (eps.Count == 0)
            {
                return;
            }

            ProcessedEpisode ep = eps[0];

            List<ShowItem> sis = new List<ShowItem>();
            foreach (ProcessedEpisode e in eps)
            {
                sis.Add(e.Show);
            }

            mLastEpClicked = ep;
            mLastShowsClicked = sis;
            mLastSeasonClicked = ep?.AppropriateSeason;
            mLastActionsClicked = null;
            //BuildRightClickMenu(pt);
        }
        /*
        private void MenuGuideAndTvdb(bool addSep)
        {
            if (mLastShowsClicked is null || mLastShowsClicked.Count != 1)
            {
                return; // nothing or multiple selected
            }

            ShowItem si = mLastShowsClicked[0];
            Season seas = mLastSeasonClicked;
            ProcessedEpisode ep = mLastEpClicked;

            if (addSep)
            {
                showRightClickMenu.Items.Add(new ToolStripSeparator());
            }

            if (ep != null)
            {
                if (si != null)
                {
                    AddRcMenuItem("Episode Guide", RightClickCommands.kEpisodeGuideForShow);
                }
                AddRcMenuItem("Visit thetvdb.com", RightClickCommands.kVisitTvdbEpisode);
            }
            else if (seas != null)
            {
                if (si != null)
                {
                    AddRcMenuItem("Episode Guide", RightClickCommands.kEpisodeGuideForShow);
                }
                AddRcMenuItem("Visit thetvdb.com", RightClickCommands.kVisitTvdbSeason);
            }
            else if (si != null)
            {
                AddRcMenuItem("Episode Guide", RightClickCommands.kEpisodeGuideForShow);
                AddRcMenuItem("Visit thetvdb.com", RightClickCommands.kVisitTvdbSeries);
            }
            else
            {
                //nothing to add
            }
        }

        private void MenuShowAndEpisodes()
        {
            ShowItem si = mLastShowsClicked != null && mLastShowsClicked.Count > 0
                ? mLastShowsClicked[0]
                : null;

            Season seas = mLastSeasonClicked;
            ProcessedEpisode ep = mLastEpClicked;

            if (si != null)
            {
                AddRcMenuItem("Force Refresh", RightClickCommands.kForceRefreshSeries);
                AddRcMenuItem("Update Images", RightClickCommands.kUpdateImages);
                showRightClickMenu.Items.Add(new ToolStripSeparator());

                string scanText = mLastShowsClicked.Count > 1
                    ? "Scan Multiple Shows"
                    : "Scan \"" + si.ShowName + "\"";
                AddRcMenuItem(scanText, RightClickCommands.kScanSpecificSeries);

                if (mLastShowsClicked != null && mLastShowsClicked.Count == 1)
                {
                    AddRcMenuItem("When to Watch", RightClickCommands.kWhenToWatchSeries);
                    AddRcMenuItem("Edit Show", RightClickCommands.kEditShow);
                    AddRcMenuItem("Delete Show", RightClickCommands.kDeleteShow);
                }
            }

            if (seas != null && mLastShowsClicked != null && mLastShowsClicked.Count == 1)
            {
                AddRcMenuItem("Edit " + Season.UIFullSeasonWord(seas.SeasonNumber), RightClickCommands.kEditSeason);
            }

            if (ep != null && mLastShowsClicked != null && mLastShowsClicked.Count == 1)
            {
                List<FileInfo> fl = FinderHelper.FindEpOnDisk(null, ep);
                if (fl.Count <= 0)
                {
                    return;
                }

                showRightClickMenu.Items.Add(new ToolStripSeparator());

                int n = mLastFl.Count;
                foreach (FileInfo fi in fl)
                {
                    mLastFl.Add(fi);
                    ToolStripMenuItem tsi = new ToolStripMenuItem("Watch: " + fi.FullName)
                    {
                        Tag = (int)RightClickCommands.kWatchBase + n
                    };

                    showRightClickMenu.Items.Add(tsi);
                }
            }
            else if (seas != null && si != null && mLastShowsClicked != null && mLastShowsClicked.Count == 1)
            {
                ToolStripMenuItem tsis = new ToolStripMenuItem("Watch Epsiodes");

                // for each episode in season, find it on disk
                foreach (ProcessedEpisode epds in si.SeasonEpisodes[seas.SeasonNumber])
                {
                    List<FileInfo> fl = FinderHelper.FindEpOnDisk(null, epds);
                    if (fl.Count <= 0)
                    {
                        continue;
                    }

                    int n = mLastFl.Count;
                    foreach (FileInfo fi in fl)
                    {
                        mLastFl.Add(fi);
                        ToolStripMenuItem tsisi = new ToolStripMenuItem("Watch: " + fi.FullName)
                        {
                            Tag = (int)RightClickCommands.kWatchBase + n
                        };

                        int n1 = n;
                        tsisi.Click += (s, ev) => {
                            WatchEpisode(n1);
                        };

                        n++;
                        tsis.DropDownItems.Add(tsisi);
                    }
                }

                if (tsis.DropDownItems.Count > 0)
                {
                    showRightClickMenu.Items.Add(new ToolStripSeparator());
                    showRightClickMenu.Items.Add(tsis);
                }
            }
        }

        private void MenuFolders(LvResults lvr)
        {
            if (mLastShowsClicked is null || mLastShowsClicked.Count != 1)
            {
                return;
            }

            ShowItem si = mLastShowsClicked != null && mLastShowsClicked.Count > 0
                ? mLastShowsClicked[0]
                : null;

            Season seas = mLastSeasonClicked;
            ProcessedEpisode ep = mLastEpClicked;
            List<string> added = new List<string>();

            if (ep != null)
            {
                Dictionary<int, List<string>> afl = ep.Show.AllExistngFolderLocations();
                if (afl.ContainsKey(ep.AppropriateSeasonNumber))
                {
                    AddFolders(afl[ep.AppropriateSeasonNumber], added);
                }
            }
            else if (seas != null && si != null)
            {
                Dictionary<int, List<string>> folders = si.AllExistngFolderLocations();

                if (folders.ContainsKey(seas.SeasonNumber))
                {
                    AddFolders(folders[seas.SeasonNumber], added);
                }
            }

            if (si != null)
            {
                AddFoldersSubMenu(si.AllExistngFolderLocations().Values.SelectMany(l => l).ToList(), added);
            }

            if (lvr is null)
            {
                return;
            }

            {
                AddFoldersSubMenu(lvr.FlatList.Select(sli => sli.TargetFolder), added);
            }
        }

        private void AddFolders([NotNull] IEnumerable<string> foldersList, [NotNull] ICollection<string> alreadyAdded)
        {
            int n = mFoldersToOpen.Count;
            bool first = true;
            foreach (string folder in foldersList
                .Where(folder => !string.IsNullOrEmpty(folder))
                .Where(Directory.Exists)
                .Where(folder => !alreadyAdded.Contains(folder)))
            {
                alreadyAdded.Add(folder); // don't show the same folder more than once
                if (first)
                {
                    showRightClickMenu.Items.Add(new ToolStripSeparator());
                    first = false;
                }

                ToolStripMenuItem tsi = new ToolStripMenuItem("Open: " + folder);
                mFoldersToOpen.Add(folder);
                tsi.Tag = (int)RightClickCommands.kOpenFolderBase + n;
                n++;
                showRightClickMenu.Items.Add(tsi);
            }
        }

        private void AddFoldersSubMenu([NotNull] IEnumerable<string> foldersList, [NotNull] ICollection<string> alreadyAdded)
        {
            int n = mFoldersToOpen.Count;

            ToolStripMenuItem tsi = new ToolStripMenuItem("Open Other Folders");

            foreach (string folder in foldersList.Where(folder => !string.IsNullOrEmpty(folder) && Directory.Exists(folder) && !alreadyAdded.Contains(folder)))
            {
                alreadyAdded.Add(folder); // don't show the same folder more than once
                ToolStripMenuItem tssi = new ToolStripMenuItem("Open: " + folder);
                mFoldersToOpen.Add(folder);
                tssi.Tag = (int)RightClickCommands.kOpenFolderBase + n;
                int n1 = n;
                tssi.Click += (s, ev) => {
                    OpenFolderForShow(n1);
                };
                n++;
                tsi.DropDownItems.Add(tssi);
            }

            if (tsi.DropDownItems.Count > 0)
            {
                showRightClickMenu.Items.Add(new ToolStripSeparator());
                showRightClickMenu.Items.Add(tsi);
            }
        }

        private void BuildRightClickMenu(Point pt)
        {
            showRightClickMenu.Items.Clear();
            mFoldersToOpen = new List<string>();
            mLastFl = new List<FileInfo>();

            MenuGuideAndTvdb(false);
            MenuShowAndEpisodes();
            MenuFolders(null);

            showRightClickMenu.Show(pt);
        }

        private void showRightClickMenu_ItemClicked(object sender, [NotNull] ToolStripItemClickedEventArgs e)
        {
            showRightClickMenu.Close();

            if (e.ClickedItem.Tag is null)
            {
                mLastEpClicked = null;
                return;
            }

            RightClickCommands n = (RightClickCommands)e.ClickedItem.Tag;

            ShowItem si = mLastShowsClicked != null && mLastShowsClicked.Count > 0
                ? mLastShowsClicked[0]
                : null;

            switch (n)
            {
                case RightClickCommands.kEpisodeGuideForShow: // epguide
                    if (mLastEpClicked != null)
                    {
                        GotoEpguideFor(mLastEpClicked, true);
                    }
                    else
                    {
                        if (si != null)
                        {
                            GotoEpguideFor(si, true);
                        }
                    }

                    break;

                case RightClickCommands.kVisitTvdbEpisode: // thetvdb.com
                    {
                        TvdbFor(mLastEpClicked);
                        break;
                    }

                case RightClickCommands.kVisitTvdbSeason:
                    {
                        TvdbFor(mLastSeasonClicked);
                        break;
                    }

                case RightClickCommands.kVisitTvdbSeries:
                    {
                        TvdbFor(si);
                        break;
                    }
                case RightClickCommands.kScanSpecificSeries:
                    {
                        if (mLastShowsClicked != null)
                        {
                            UiScan(mLastShowsClicked, false, TVSettings.ScanType.SingleShow);
                            tabControl1.SelectTab(tbAllInOne);
                        }
                        break;
                    }

                case RightClickCommands.kWhenToWatchSeries: // when to watch
                    {
                        int code = -1;
                        if (mLastEpClicked != null)
                        {
                            code = mLastEpClicked.TheSeries.TvdbCode;
                        }

                        if (si != null)
                        {
                            code = si.TvdbCode;
                        }

                        GotoWtwFor(code);

                        break;
                    }
                case RightClickCommands.kForceRefreshSeries:
                    if (si != null)
                    {
                        ForceRefresh(mLastShowsClicked, false);
                    }

                    break;
                case RightClickCommands.kUpdateImages:
                    if (si != null)
                    {
                        UpdateImages(mLastShowsClicked);
                        tabControl1.SelectTab(tbAllInOne);
                    }

                    break;
                case RightClickCommands.kEditShow:
                    if (si != null)
                    {
                        EditShow(si);
                    }

                    break;
                case RightClickCommands.kDeleteShow:
                    if (si != null)
                    {
                        DeleteShow(si);
                    }

                    break;
                case RightClickCommands.kEditSeason:
                    if (si != null)
                    {
                        EditSeason(si, mLastSeasonClicked.SeasonNumber);
                    }

                    break;
                case RightClickCommands.kBtSearchFor:
                    {
                        foreach (ListViewItem lvi in lvAction.SelectedItems)
                        {
                            ItemMissing m = (ItemMissing)lvi.Tag;
                            if (m != null)
                            {
                                TVDoc.SearchForEpisode(m.Episode);
                            }
                        }
                    }

                    break;
                case RightClickCommands.kActionAction:
                    ActionAction(false, false);
                    break;
                case RightClickCommands.kActionRevert:
                    Revert(false);
                    break;
                case RightClickCommands.kActionBrowseForFile:
                    if (mLastActionsClicked != null && mLastActionsClicked.Count > 0)
                    {
                        BrowseForMissingItem((ItemMissing)mLastActionsClicked[0]);
                        mLastActionsClicked = null;
                    }
                    break;
                case RightClickCommands.kActionIgnore:
                    IgnoreSelected();
                    break;
                case RightClickCommands.kActionIgnoreSeason:
                    {
                        // add season to ignore list for each show selected
                        IgnoreSelectedSeasons();

                        mLastActionsClicked = null;
                        FillActionList();
                        break;
                    }
                case RightClickCommands.kActionDelete:
                    ActionDeleteSelected();
                    break;
                default:
                    {
                        //The entries immediately above WatchBase are the Watchxx commands and the paths are stored in mLastFL
                        if (n >= RightClickCommands.kWatchBase && n < RightClickCommands.kOpenFolderBase)
                        {
                            WatchEpisode(n - RightClickCommands.kWatchBase);
                        }
                        else if (n >= RightClickCommands.kOpenFolderBase && n < RightClickCommands.kSearchForBase)
                        {
                            OpenFolderForShow(n - RightClickCommands.kOpenFolderBase);
                            return;
                        }
                        else if (n >= RightClickCommands.kSearchForBase)
                        {
                            SearchFor(n - RightClickCommands.kSearchForBase);
                            return;
                        }
                        else
                        {
                            Debug.Fail("Unknown right-click action " + n);
                        }

                        break;
                    }
            }
            mLastEpClicked = null;
        }
        */
/*
        private void BrowseForMissingItem([CanBeNull] ItemMissing mi)
        {
            if (mi is null)
            {
                return;
            }

            // browse for mLastActionClicked
            openFile.Filter = "Video Files|" +
                              TVSettings.Instance.GetVideoExtensionsString()
                                  .Replace(".", "*.") +
                              "|All Files (*.*)|*.*";

            if (openFile.ShowDialog() != MessageBoxResult.OK)
            {
                return;
            }

            ManuallyAddFileForItem(mi, openFile.FileName);
        }
*/

        private void ManuallyAddFileForItem([NotNull] ItemMissing mi, string fileName)
        {
            // make new Item for copying/moving to specified location
            FileInfo from = new FileInfo(fileName);
            FileInfo to = new FileInfo(mi.TheFileNoExt + from.Extension);
            mDoc.TheActionList.Add(
                new ActionCopyMoveRename(
                    TVSettings.Instance.LeaveOriginals
                        ? ActionCopyMoveRename.Op.copy
                        : ActionCopyMoveRename.Op.move, from, to
                    , mi.Episode, true, mi));

            // and remove old Missing item
            mDoc.TheActionList.Remove(mi);

            // if we're copying/moving a file across, we might also want to make a thumbnail or NFO for it
            DownloadIdentifiersController di = new DownloadIdentifiersController();
            mDoc.TheActionList.Add(di.ProcessEpisode(mi.Episode, to));

            //If keep together is active then we may want to copy over related files too
            if (TVSettings.Instance.KeepTogether)
            {
                FileFinder.KeepTogether(mDoc.TheActionList, false, true);
            }

            FillActionList();
        }

        private void OpenFolderForShow(int foldernum)
        {
            if (mFoldersToOpen != null && foldernum >= 0 && foldernum < mFoldersToOpen.Count)
            {
                string folder = mFoldersToOpen[foldernum];

                if (Directory.Exists(folder))
                {
                    Helpers.SysOpen(folder);
                }
            }
        }

        private void SearchFor(int num)
        {
            string url = TVSettings.Instance.TheSearchers.Url(num);

            ProcessedEpisode epi = mLastEpClicked;
            if (epi != null)
            {
                Helpers.SysOpen(CustomEpisodeName.NameForNoExt(epi, url, true));
                return;
            }

            //LvResults lvr = new LvResults(lvAction, false);

            //foreach (Item i in lvr.FlatList)
            {
                //if (i is ItemMissing miss)
                {
                    //Helpers.SysOpen(CustomEpisodeName.NameForNoExt(miss.Episode, url, true));
                }
            }
        }

        private void WatchEpisode(int wn)
        {
            if (mLastFl != null && wn >= 0 && wn < mLastFl.Count)
            {
                Helpers.SysOpen(mLastFl[wn].FullName);
            }
        }

        private void IgnoreSelectedSeasons()
        {
            if (mLastActionsClicked != null && mLastActionsClicked.Count > 0)
            {
                foreach (Item ai in mLastActionsClicked)
                {
                    Item er = ai;
                    if (er?.Episode is null)
                    {
                        continue;
                    }

                    int snum = er.Episode.AppropriateSeasonNumber;

                    if (!er.Episode.Show.IgnoreSeasons.Contains(snum))
                    {
                        er.Episode.Show.IgnoreSeasons.Add(snum);
                    }

                    // remove all other episodes of this season from the Action list
                    ItemList remove = new ItemList();
                    foreach (Item action in mDoc.TheActionList)
                    {
                        Item er2 = action;
                        if (er2?.Episode is null)
                        {
                            continue;
                        }

                        if (er2.Episode.AppropriateSeasonNumber != snum)
                        {
                            continue;
                        }

                        if (er2.TargetFolder == er.TargetFolder) //ie if they are for the same series
                        {
                            remove.Add(action);
                        }
                    }

                    foreach (Item action in remove)
                    {
                        mDoc.TheActionList.Remove(action);
                    }

                    if (remove.Count > 0)
                    {
                        mDoc.SetDirty();
                    }
                }

                FillMyShows();
            }
        }

        private void GotoWtwFor(int tvdbSeriesCode)
        {
            if (tvdbSeriesCode == -1)
            {
                return;
            }

            tabControl1.SelectedItem = tbWTW;
            foreach (object lvi in lvWhenToWatch.Items)
            {
                //ProcessedEpisode ei = (ProcessedEpisode)lvi.Tag;
                //lvi.Selected = ei != null && ei.TheSeries.TvdbCode == tvdbSeriesCode;
            }
            lvWhenToWatch.Focus();
        }

/*
        private void tabControl1_DoubleClick(object sender, EventArgs e)
        {
            if ((tabControl1.SelectedItem as TabItem) == tbMyShows)
            {
                bnMyShowsRefresh_Click(null, null);
            }
            else if ((tabControl1.SelectedItem as TabItem) == tbWTW)
            {
                bnWhenToWatchCheck_Click(null, null);
            }
            else if ((tabControl1.SelectedItem as TabItem) == tbAllInOne)
            {
                bnActionRecentCheck_Click(null, null);
            }
            else
            {
                throw new ArgumentOutOfRangeException();
            }
        }
*/

/*
        private void folderRightClickMenu_ItemClicked(object sender,
            [NotNull] ToolStripItemClickedEventArgs e)
        {
            if ((int)e.ClickedItem.Tag == 0)
            {
                Helpers.SysOpen(mLastFolderClicked);
            }
        }
*/

/*
        private void lvWhenToWatch_MouseClick(object sender, [NotNull] MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            if (lvWhenToWatch.SelectedItems.Count == 0)
            {
                return;
            }

            Point pt = lvWhenToWatch.PointToScreen(new Point(e.X, e.Y));
            List<ProcessedEpisode> eis = new List<ProcessedEpisode>();
            foreach (ListViewItem lvi in lvWhenToWatch.SelectedItems)
            {
                eis.Add(lvi.Tag as ProcessedEpisode);
            }

            WtwRightClickOnShow(eis, pt);
        }
*/

        private void preferencesToolStripMenuItem_Click(object sender, EventArgs e) => DoPrefs(false);

        private void DoPrefs(bool scanOptions)
        {
            MoreBusy(); // no background download while preferences are open!
            mDoc.PreventAutoScan("Preferences are open");

/*
            Preferences pref = new Preferences(mDoc, scanOptions, CurrentlySelectedSeason());
            if (pref.ShowDialog() == MessageBoxResult.OK)
            {
                mDoc.SetDirty();
                ShowHideNotificationIcon();
                FillWhenToWatchList();
                ShowInTaskbar = TVSettings.Instance.ShowInTaskbar;
                FillEpGuideHtml();
                mAutoFolderMonitor.SettingsChanged(TVSettings.Instance.MonitorFolders);
                betaToolsToolStripMenuItem.Visible = TVSettings.Instance.IncludeBetaUpdates();
                ForceRefresh(null, false);
            }
*/

            mDoc.AllowAutoScan();
            LessBusy();
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                mDoc.WriteXMLSettings();
                TheTVDB.Instance.SaveCache();
                if (!SaveLayoutXml())
                {
                    Logger.Error("Failed to Save Layout Configuration Files");
                }
            }
            catch (Exception ex)
            {
                Exception e2 = ex;
                while (e2.InnerException != null)
                {
                    e2 = e2.InnerException;
                }

                Logger.Error(e2, "Failed to Save Configuration Files");
                string m2 = e2.Message;
                MessageBox.Show(this,
                    ex.Message + "\r\n\r\n" +
                    m2 + "\r\n\r\n" +
                    ex.StackTrace,
                    "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UI_SizeChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                //mLastNonMaximizedSize = Size;
            }

            if (WindowState == WindowState.Minimized && !TVSettings.Instance.ShowInTaskbar)
            {
                Hide();
            }
        }

        private void UI_LocationChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                //mLastNonMaximizedLocation = Location;
            }
        }

        private void statusTimer_Tick(object sender, EventArgs e)
        {
            int n = mDoc.DownloadsRemaining();

            txtStatusLabel.Visibility = (n != 0 || TVSettings.Instance.BGDownload) ? Visibility.Visible:Visibility.Hidden;

            mainViewModel.Downloading = n != 0 ? TheTVDB.Instance.CurrentDLTask : "Idle";

            if (busy == 0)
            {
                if (n == 0 && lastDlRemaining > 0)
                {
                    // we've just finished a bunch of background downloads
                    TheTVDB.Instance.SaveCache();
                    RefreshWTW(false, true);

                    //backgroundDownloadNowToolStripMenuItem.Enabled = true;
                }

                lastDlRemaining = n;
            }
        }

        private void backgroundDownloadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            TVSettings.Instance.BGDownload = !TVSettings.Instance.BGDownload;
            //backgroundDownloadToolStripMenuItem.Checked = TVSettings.Instance.BGDownload;
            statusTimer_Tick(null, null);
            mDoc.SetDirty();

            if (TVSettings.Instance.BGDownload)
            {
                //BGDownloadTimer.Start();
            }
            else
            {
                //BGDownloadTimer.Stop();
            }
        }

        private async void UpdateTimer_Tick(object sender, EventArgs e)
        {
            //UpdateTimer.Stop();

            Dispatcher uiDisp = Dispatcher.CurrentDispatcher;

            Task<Release> tuv = VersionUpdater.CheckForUpdatesAsync();
            Release result = await tuv.ConfigureAwait(false);

            uiDisp.Invoke(() => NotifyUpdates(result, false));
        }

/*
        private void BGDownloadTimer_Tick(object sender, EventArgs e)
        {
            if (busy != 0)
            {
                BGDownloadTimer.Interval = 10000; // come back in 10 seconds
                BGDownloadTimer.Start();
                Logger.Info("BG Download is busy - try again in 10 seconds");
                return;
            }

            BGDownloadTimer.Interval = BgdlLongInterval(); // after first time (10 seconds), put up to 60 minutes
            BGDownloadTimer.Start();

            if (TVSettings.Instance.BGDownload && mDoc.DownloadsRemaining() == 0)
            // only do auto-download if don't have stuff to do already
            {
                BackgroundDownloadNow();
            }
        }
*/

        private void BGDownloadTimer_QuickFire()
        {
            //BGDownloadTimer.Stop();
            //BGDownloadTimer.Interval = 1000;
            //BGDownloadTimer.Start();
        }

        private void backgroundDownloadNowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (TVSettings.Instance.OfflineMode)
            {
                MessageBoxResult res = MessageBox.Show("Ignore offline mode and download anyway?",
                    "Background Download", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (res != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            BackgroundDownloadNow();
        }

        private void BackgroundDownloadNow()
        {
            //BGDownloadTimer.Stop();
            //BGDownloadTimer.Start();

            mDoc.DoDownloadsBG();

            statusTimer_Tick(null, null);
        }

        private void offlineOperationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!TVSettings.Instance.OfflineMode)
            {
                if (MessageBox.Show("Are you sure you wish to go offline?", "TVRename", MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) == MessageBoxResult.No)
                {
                    return;
                }
            }

            TVSettings.Instance.OfflineMode = !TVSettings.Instance.OfflineMode;
            //offlineOperationToolStripMenuItem.Checked = TVSettings.Instance.OfflineMode;
            mDoc.SetDirty();
        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (Equals(tabControl1.SelectedItem, tbMyShows))
            {
               //FillEpGuideHtml();
            }
        }

        private void bugReportToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //BugReport br = new BugReport(mDoc);
            //br.ShowDialog();
        }

        private void ShowHideNotificationIcon()
        {
            //notifyIcon1.Visible = TVSettings.Instance.NotificationAreaIcon && !mDoc.Args.Hide;
        }

        private void statisticsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //StatsWindow sw = new StatsWindow(mDoc.Stats());
            //sw.ShowDialog();
        }


/*        [NotNull]
        private TreeViewItem AddShowItemToTree([NotNull] ShowItem si)
        {
            SeriesInfo ser;
            lock (TheTVDB.SERIES_LOCK)
            {
                ser = TheTVDB.Instance.GetSeries(si.TvdbCode);
            }

            TreeViewItem n=null;

            if (ser != null)
            {
                List<int> theKeys = si.DvdOrder
                    ? new List<int>(ser.DvdSeasons.Keys)
                    : new List<int>(ser.AiredSeasons.Keys);

                theKeys.Sort();

                SeasonFilter sf = TVSettings.Instance.SeasonFilter;
                foreach (int snum in theKeys)
                {
                    Season s = si.DvdOrder ? ser.DvdSeasons[snum] : ser.AiredSeasons[snum];

                    //Ignore the season if it is filtered out
                    if (!sf.Filter(si, s))
                    {
                        continue;
                    }

                    if (snum == 0 && TVSettings.Instance.IgnoreAllSpecials)
                    {
                        continue;
                    }

                    string nodeTitle = ShowHtmlHelper.SeasonName(si, snum);

                    TreeViewItem n2 = new TreeViewItem
                    {
                        Name = nodeTitle,
                        Tag = s,
                        Foreground = new SolidColorBrush(SeasonColour(si, snum, s))
                    };

                    n.Items.Add(n2);
                }
            }

            MyShowTree.Items.Add(n);

            return n;
        }*/

        private static Color SeasonColour(ShowItem si, int snum, Season s)
        {
            if (si.IgnoreSeasons.Contains(snum))
            {
                return Colors.Gray;
            }
            else
            {
                if (TVSettings.Instance.ShowStatusColors != null)
                {
                    Color? nodeColor =
                        TVSettings.Instance.ShowStatusColors.GetEntry(true, false,
                            s.Status(si.GetTimeZone()).ToString());

                    return nodeColor??Colors.Black;
                }
            }

            return Colors.Black;
        }


        private void SelectSeason(Season seas)
        {
/*            foreach (TreeViewItem n in MyShowTree.Items)
            {
                foreach (TreeViewItem n2 in n.Nodes)
                {
                    if (TreeViewItemToSeason(n2) == seas)
                    {
                        n2.EnsureVisible();
                        MyShowTree.SelectedNde = n2;
                        return;
                    }
                }
            }

            FillEpGuideHtml(null);*/
        }

        private void SelectShow(ShowItem si)
        {
/*            foreach (TreeViewItem n in MyShowTree.Nodes)
            {
                if (TreeViewItemToShowItem(n) == si)
                {
                    n.EnsureVisible();
                    MyShowTree.SelectedNode = n;
                    //FillEpGuideHTML();
                    return;
                }
            }

            FillEpGuideHtml(null);*/
        }

        private void bnMyShowsAdd_Click(object sender, EventArgs e)
        {
            Logger.Info("****************");
            Logger.Info("Adding New Show");
            MoreBusy();
            mDoc.PreventAutoScan("Add Show");
            ShowItem si = new ShowItem();

/*            AddEditShow aes = new AddEditShow(si);
            MessageBoxResult dr = aes.ShowDialog();
            if (dr == MessageBoxResult.OK)
            {
                lock (TheTVDB.SERIES_LOCK)
                {
                    mDoc.Library.Add(si);
                }

                ShowAddedOrEdited(false, false);
                SelectShow(si);

                Logger.Info("Added new show called {0}", si.ShowName);
            }
            else
            {
                Logger.Info("Cancelled adding new show");
            }*/

            ShowAddedOrEdited(true, false);

            LessBusy();
            mDoc.AllowAutoScan();
        }

        private void ShowAddedOrEdited(bool download, bool unattended)
        {
            mDoc.SetDirty();
            RefreshWTW(download, unattended);
            mDoc.ReindexLibrary();
            FillMyShows();

            mDoc.ExportShowInfo(); //Save shows list to disk
        }

        private void bnMyShowsDelete_Click(object sender, EventArgs e)
        {
//            TreeViewItem n = (TreeViewItem)MyShowTree.SelectedItem;
//            ShowItem si = TreeViewItemToShowItem(n);
//            if (si is null)
//            {
//                return;
//            }
//
//            DeleteShow(si);
        }

        private void DeleteShow([NotNull] ShowItem si)
        {
            MessageBoxResult res = MessageBox.Show(
                "Remove show \"" + si.ShowName + "\".  Are you sure?", "Confirmation", MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes)
            {
                return;
            }

            if ((Directory.Exists(si.AutoAddFolderBase)) && TVSettings.Instance.DeleteShowFromDisk)
            {
                MessageBoxResult res3 = MessageBox.Show(
                    $"Remove folder \"{si.AutoAddFolderBase}\" from disk?",
                    "Confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (res3 == MessageBoxResult.Yes)
                {
                    Logger.Info($"Recycling {si.AutoAddFolderBase} as part of the removal of {si.ShowName}");
//                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(si.AutoAddFolderBase,
//                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
//                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
            }

            Logger.Info($"User asked to remove {si.ShowName} - removing now");
            mDoc.Library.Remove(si);
            ShowAddedOrEdited(false, false);
        }

/*
        private void bnMyShowsEdit_Click(object sender, EventArgs e)
        {
            TreeViewItem n = (TreeViewItem)MyShowTree.SelectedItem;
            if (n is null)
            {
                return;
            }

            Season seas = TreeViewItemToSeason(n);
            if (seas != null)
            {
                ShowItem si = TreeViewItemToShowItem(n);
                if (si != null)
                {
                    EditSeason(si, seas.SeasonNumber);
                }

                return;
            }

            ShowItem si2 = TreeViewItemToShowItem(n);
            if (si2 != null)
            {
                EditShow(si2);
            }
        }
*/

        internal void EditSeason([NotNull] ShowItem si, int seasnum)
        {
            MoreBusy();
            mDoc.PreventAutoScan("Edit Season");

            lock (TheTVDB.SERIES_LOCK)
            {
                SeriesInfo ser = TheTVDB.Instance.GetSeries(si.TvdbCode);
                if (ser is null)
                {
                    Logger.Error($"Asked to edit season {seasnum} of {si.ShowName}, but the SeriesInfo doesn't exist");
                }
                else
                {
                    List<ProcessedEpisode> pel = ShowLibrary.GenerateEpisodes(si, ser, seasnum, false);

                    //EditRules er = new EditRules(si, pel, seasnum, TVSettings.Instance.NamingStyle);
                    //MessageBoxResult dr = er.ShowDialog();
                    //if (dr == MessageBoxResult.OK)
                    //{
                        //ShowAddedOrEdited(false, false);
                        //Dictionary<int, Season> seasonsToUse = si.DvdOrder ? ser.DvdSeasons : ser.AiredSeasons;
                        //SelectSeason(seasonsToUse[seasnum]);
                    //}
                }
            }

            mDoc.AllowAutoScan();
            LessBusy();
        }

        internal void EditShow([NotNull] ShowItem si)
        {
            MoreBusy();
            mDoc.PreventAutoScan("Edit Show");

            int oldCode = si.TvdbCode;

            //AddEditShow aes = new AddEditShow(si);

            //MessageBoxResult dr = aes.ShowDialog();

            //if (dr == MessageBoxResult.OK)
            //{
              //  ShowAddedOrEdited(si.TvdbCode != oldCode, false);
//                SelectShow(si);

                //Logger.Info("Modified show called {0}", si.ShowName);
//            }

            mDoc.AllowAutoScan();
            LessBusy();
        }

        internal void ForceRefresh(List<ShowItem> sis, bool unattended)
        {
            mDoc.ForceRefresh(sis, unattended, WindowState == WindowState.Minimized);
            FillMyShows();
            //FillEpGuideHtml();
            RefreshWTW(false, unattended);
        }

        private void UpdateImages([CanBeNull] List<ShowItem> sis)
        {
            if (sis != null)
            {
                ForceRefresh(sis, false);

                foreach (ShowItem si in sis)
                {
                    //update images for the showitem
                    mDoc.ForceUpdateImages(si);
                }

                FillActionList();
            }
        }

/*
        private void bnMyShowsRefresh_Click(object sender, EventArgs e)
        {
            if (ModifierKeys == Keys.Control)
            {
                // nuke currently selected show to force getting it fresh
                TreeViewItem n = (TreeViewItem)MyShowTree.SelectedItem;
                ShowItem si = TreeViewItemToShowItem(n);
                ForceRefresh(new List<ShowItem> { si }, false);
            }
            else
            {
                ForceRefresh(null, false);
            }
        }
*/

/*
        private void MyShowTree_AfterSelect(object sender, [NotNull] TreeViewEventArgs e)
        {
            FillEpGuideHtml(e.Node);
            bool showSelected = MyShowTree.SelectedNode != null;
            bnMyShowsEdit.Enabled = showSelected;
            bnMyShowsDelete.Enabled = showSelected;
        }
*/

/*
        private void MyShowTree_MouseClick(object sender, [NotNull] MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            MyShowTree.SelectedNode = MyShowTree.GetNodeAt(e.X, e.Y);

            Point pt = MyShowTree.PointToScreen(new Point(e.X, e.Y));
            TreeViewItem n = (TreeViewItem)MyShowTree.SelectedItem;

            if (n is null)
            {
                return;
            }

            ShowItem si = TreeViewItemToShowItem(n);
            Season seas = TreeViewItemToSeason(n);

            if (seas != null)
            {
                RightClickOnMyShows(seas, pt);
            }
            else if (si != null)
            {
                RightClickOnMyShows(si, pt);
            }
        }
*/

        private void quickstartGuideToolStripMenuItem_Click(object sender, EventArgs e) => ShowQuickStartGuide();

/*
        private Season CurrentlySelectedSeason()
        {
            Season currentSeas = TreeViewItemToSeason((TreeViewItem)MyShowTree.SelectedItem);
            if (currentSeas != null)
            {
                return currentSeas;
            }

            ShowItem currentShow = TreeViewItemToShowItem((TreeViewItem)MyShowTree.SelectedItem);
            if (currentShow != null)
            {
                foreach (KeyValuePair<int, Season> s in currentShow.AppropriateSeasons())
                {
                    //Find first season we can
                    return s.Value;
                }
            }

            foreach (ShowItem si in mDoc.Library.GetShowItems())
            {
                foreach (KeyValuePair<int, Season> s in si.AppropriateSeasons())
                {
                    //Find first season we can
                    return s.Value;
                }
            }
            return null;
        }
*/

//        [CanBeNull]
//        private List<ProcessedEpisode> CurrentlySelectedPel()
//        {
////            Season currentSeas = TreeViewItemToSeason(MyShowTree.SelectedNode);
////            ShowItem currentShow = TreeViewItemToShowItem(MyShowTree.SelectedNode);
//
///*            int snum = currentSeas?.SeasonNumber ?? 1;
//            List<ProcessedEpisode> pel = null;
///*            if (currentShow != null && currentShow.SeasonEpisodes.ContainsKey(snum))
//            {
//                pel = currentShow.SeasonEpisodes[snum];
//            }
//            else if (currentShow?.SeasonEpisodes.First() != null)
//            {
//                pel = currentShow.SeasonEpisodes.First().Value;
//            }
//            else
//            {
//                foreach (ShowItem si in mDoc.Library.GetShowItems())
//                {
//                    foreach (KeyValuePair<int, List<ProcessedEpisode>> kvp in si.SeasonEpisodes)
//                    {
//                        pel = kvp.Value;
//                        break;
//                    }
//
//                    if (pel != null)
//                    {
//                        break;
//                    }
//                }
//            }#1#
//
//            return pel;*/
//        }

/*
        private void filenameTemplateEditorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            CustomEpisodeName cn = new CustomEpisodeName(TVSettings.Instance.NamingStyle.StyleString);
            CustomNameDesigner cne = new CustomNameDesigner(CurrentlySelectedPel(), cn);
            MessageBoxResult dr = cne.ShowDialog();
            if (dr == MessageBoxResult.OK)
            {
                TVSettings.Instance.NamingStyle = cn;
                mDoc.SetDirty();
            }
        }
*/

/*
        private void searchEnginesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<ProcessedEpisode> pel = CurrentlySelectedPel();

            AddEditSearchEngine aese = new AddEditSearchEngine(TVDoc.GetSearchers(),
                pel != null && pel.Count > 0 ? pel[0] : null);

            MessageBoxResult dr = aese.ShowDialog();
            if (dr == MessageBoxResult.OK)
            {
                mDoc.SetDirty();
                UpdateSearchButtons();
            }
        }
*/

/*
        private void filenameProcessorsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowItem currentShow = TreeViewItemToShowItem(MyShowTree.SelectedNode);
            string theFolder = GetFolderForShow(currentShow);

            if (string.IsNullOrWhiteSpace(theFolder) && TVSettings.Instance.DownloadFolders.Count > 0)
            {
                theFolder = TVSettings.Instance.DownloadFolders.First();
            }

            AddEditSeasEpFinders d = new AddEditSeasEpFinders(TVSettings.Instance.FNPRegexs, mDoc.Library.GetShowItems(), currentShow, theFolder);

            MessageBoxResult dr = d.ShowDialog();
            if (dr == MessageBoxResult.OK)
            {
                mDoc.SetDirty();
                TVSettings.Instance.FNPRegexs = d.OutputRegularExpressions;
            }
        }
*/

        [NotNull]
        private static string GetFolderForShow([CanBeNull] ShowItem currentShow)
        {
            if (currentShow is null)
            {
                return string.Empty;
            }

            foreach (List<string> folders in currentShow.AllExistngFolderLocations().Values)
            {
                foreach (string folder in folders)
                {
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    {
                        return folder;
                    }
                }
            }

            return string.Empty;
        }

        private void actorsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //new ActorsGrid(mDoc).ShowDialog();
        }

        private void quickTimer_Tick(object sender, EventArgs e)
        {
            //quickTimer.Stop();
            //ProcessArgs();
        }

/*
        private void bnMyShowsCollapse_Click(object sender, EventArgs e)
        {
            MyShowTree.BeginUpdate();
            treeExpandCollapseToggle = !treeExpandCollapseToggle;
            if (treeExpandCollapseToggle)
            {
                MyShowTree.CollapseAll();
            }
            else
            {
                MyShowTree.ExpandAll();
            }

            MyShowTree.SelectedNode?.EnsureVisible();

            MyShowTree.EndUpdate();
        }
*/

/*
        private void UI_KeyDown(object sender, [NotNull] KeyEventArgs e)
        {
            int t = -1;
            if (e.Control && e.KeyCode == Keys.D1)
            {
                t = 0;
            }
            else if (e.Control && e.KeyCode == Keys.D2)
            {
                t = 1;
            }
            else if (e.Control && e.KeyCode == Keys.D3)
            {
                t = 2;
            }
            else if (e.Control && e.KeyCode == Keys.D4)
            {
                t = 3;
            }
            else if (e.Control && e.KeyCode == Keys.D5)
            {
                t = 4;
            }
            else if (e.Control && e.KeyCode == Keys.D6)
            {
                t = 5;
            }
            else if (e.Control && e.KeyCode == Keys.D7)
            {
                t = 6;
            }
            else if (e.Control && e.KeyCode == Keys.D8)
            {
                t = 7;
            }
            else if (e.Control && e.KeyCode == Keys.D9)
            {
                t = 8;
            }
            else if (e.Control && e.KeyCode == Keys.D0)
            {
                t = 9;
            }

            if (t >= 0 && t < tabControl1.TabCount)
            {
                tabControl1.SelectedIndex = t;
                e.Handled = true;
            }
        }
*/

        private void bnActionCheck_Click(object sender, EventArgs e) => UiScan(null, false, TVSettings.ScanType.Full);

        public void QuickScan() => UiScan(null, true, TVSettings.ScanType.Quick);

        public void RecentScan() => UiScan(mDoc.Library.GetRecentShows(), true, TVSettings.ScanType.Recent);

        private void UiScan([CanBeNull] List<ShowItem> shows, bool unattended, TVSettings.ScanType st)
        {
            Logger.Info("*******************************");
            string desc = unattended ? "unattended " : "";
            string showsdesc = shows?.Count > 0 ? shows.Count.ToString() : "all";
            string scantype = st.PrettyPrint();
            Logger.Info($"Starting {desc}{scantype} Scan for {showsdesc} shows...");

            MoreBusy();
            mDoc.Scan(shows, unattended, st, WindowState == WindowState.Minimized);
            LessBusy();

            if (mDoc.ShowProblems.Any() && !unattended)
            {
                string message = mDoc.ShowProblems.Count > 1
                    ? $"Shows with Id { string.Join(",", mDoc.ShowProblems)} are not found on TVDB. Please update them"
                    : $"Show with Id {mDoc.ShowProblems.First()} is not found on TVDB. Please Update";

                MessageBoxResult result = MessageBox.Show(message, "Series No Longer Found", MessageBoxButton.OKCancel, MessageBoxImage.Error);

                if (result != MessageBoxResult.Cancel)
                {
                    foreach (int seriesId in mDoc.ShowProblems)
                    {
                        ShowItem problemShow = mDoc.Library.ShowItem(seriesId);
                        if (problemShow != null)
                        {
                            EditShow(problemShow);
                        }
                    }
                }

                mDoc.ClearShowProblems();
            }

            FillMyShows(); // scanning can download more info to be displayed in my shows
            FillActionList();
        }

/*
        [NotNull]
        private ListViewItem LviForItem(Item item)
        {
            Item sli = item;
            if (sli is null)
            {
                return new ListViewItem();
            }

            ListViewItem lvi = sli.ScanListViewItem;
            lvi.Group = lvAction.Groups[sli.ScanListViewGroup];

            if (sli.IconNumber != -1)
            {
                lvi.ImageIndex = sli.IconNumber;
            }

            lvi.Checked = true;
            lvi.Tag = sli;

            Debug.Assert(lvi.SubItems.Count <= lvAction.Columns.Count - 1);

            while (lvi.SubItems.Count < lvAction.Columns.Count - 1)
            {
                lvi.SubItems.Add(""); // pad our way to the error column
            }

            if (item is Action act && act.Error)
            {
                lvi.BackColor = Helpers.WarningColor();
            }

            lvi.SubItems.Add(item.ErrorText); // error text

            if (!(item is Action))
            {
                lvi.Checked = false;
            }

            Debug.Assert(lvi.SubItems.Count == lvAction.Columns.Count);

            return lvi;
        }
*/

/*
        private void lvAction_RetrieveVirtualItem(object sender, [NotNull] RetrieveVirtualItemEventArgs e)
        {
            Item item = mDoc.TheActionList[e.ItemIndex];
            e.Item = LviForItem(item);
        }
*/

        public void FillActionList()
        {
            /*internalCheckChange = true;

            // Save where the list is currently scrolled too
            int currentTop = lvAction.GetScrollVerticalPos();

            if (lvAction.VirtualMode)
            {
                lvAction.VirtualListSize = mDoc.TheActionList.Count;
            }
            else
            {
                lvAction.BeginUpdate();
                lvAction.Items.Clear();

                foreach (Item item in mDoc.TheActionList.ToList())
                {
                    ListViewItem lvi = LviForItem(item);
                    lvAction.Items.Add(lvi);
                }

                lvAction.EndUpdate();
            }

            // Restore the scrolled to position
            lvAction.SetScrollVerticalPos(currentTop);

            // do nice totals for each group
            int missingCount = 0;
            int renameCount = 0;
            int copyCount = 0;
            long copySize = 0;
            int moveCount = 0;
            int removeCount = 0;
            long moveSize = 0;
            int rssCount = 0;
            int downloadCount = 0;
            int metaCount = 0;
            int dlCount = 0;
            int fileMetaCount = 0;

            foreach (Item action in mDoc.TheActionList)
            {
                if (action is ItemMissing)
                {
                    missingCount++;
                }
                else if (action is ActionCopyMoveRename cmr)
                {
                    ActionCopyMoveRename.Op op = cmr.Operation;
                    if (op == ActionCopyMoveRename.Op.copy)
                    {
                        copyCount++;
                        if (cmr.From.Exists)
                        {
                            copySize += cmr.From.Length;
                        }
                    }
                    else if (op == ActionCopyMoveRename.Op.move)
                    {
                        moveCount++;
                        if (cmr.From.Exists)
                        {
                            moveSize += cmr.From.Length;
                        }
                    }
                    else if (op == ActionCopyMoveRename.Op.rename)
                    {
                        renameCount++;
                    }
                }
                else if (action is ActionDownloadImage)
                {
                    downloadCount++;
                }
                else if (action is ActionTDownload)
                {
                    rssCount++;
                }
                else if (action is ActionWriteMetadata) // base interface that all metadata actions are derived from
                {
                    metaCount++;
                }
                else if (action is ActionDateTouch)
                {
                    fileMetaCount++;
                }
                else if (action is ItemDownloading)
                {
                    dlCount++;
                }
                else if (action is ActionDeleteFile || action is ActionDeleteDirectory)
                {
                    removeCount++;
                }
            }

            lvAction.Groups[0].Header = HeaderName("Missing", missingCount);
            lvAction.Groups[1].Header = HeaderName("Rename", renameCount);
            lvAction.Groups[2].Header = HeaderName("Copy", copyCount, copySize);
            lvAction.Groups[3].Header = HeaderName("Move", moveCount, moveSize);
            lvAction.Groups[4].Header = HeaderName("Remove", removeCount);
            lvAction.Groups[5].Header = HeaderName("Download RSS", rssCount);
            lvAction.Groups[6].Header = HeaderName("Download", downloadCount);
            lvAction.Groups[7].Header = HeaderName("Media Center Metadata", metaCount);
            lvAction.Groups[8].Header = HeaderName("Update File/Directory Metadata", fileMetaCount);
            lvAction.Groups[9].Header = HeaderName("Downloading", dlCount);

            internalCheckChange = false;

            UpdateActionCheckboxes();*/
        }

        [NotNull]
        private static string HeaderName(string name, int number) => $"{name} ({PrettyPrint(number)})";

        [NotNull]
        private static string PrettyPrint(int number) => number + " " + number.ItemItems();

        [NotNull]
        private static string HeaderName(string name, int number, long filesize) => $"{name} ({PrettyPrint(number)}, {filesize.GBMB(1)})";

        private void bnActionAction_Click(object sender, EventArgs e) => ActionAction(true, false);

        public void ProcessAll() => ActionAction(true, true);

        private void ActionAction(bool checkedNotSelected, bool unattended)
        {
            mDoc.PreventAutoScan("Action Selected Items");
            //LvResults lvr = new LvResults(lvAction, checkedNotSelected);
            //mDoc.DoActions(lvr.FlatList);
            // remove items from master list, unless it had an error
            //foreach (Item i2 in new LvResults(lvAction, checkedNotSelected).FlatList)
            {
                //if (i2 != null && !lvr.FlatList.Contains(i2))
                {
                    //mDoc.TheActionList.Remove(i2);
                }
            }

            FillActionList();
            RefreshWTW(false, unattended);
            mDoc.AllowAutoScan();
        }

/*
        private void Revert(bool checkedNotSelected)
        {
            foreach (Item item in new LvResults(lvAction, checkedNotSelected).FlatList)
            {
                Action revertAction = (Action)item;
                ItemMissing m2 = revertAction.UndoItemMissing;

                if (m2 is null)
                {
                    continue;
                }

                mDoc.TheActionList.Add(m2);
                mDoc.TheActionList.Remove(revertAction);

                //We can remove any CopyMoveActions that are closely related too
                if (!(revertAction is ActionCopyMoveRename))
                {
                    continue;
                }

                ActionCopyMoveRename i2 = (ActionCopyMoveRename)item;
                List<Item> toRemove = new List<Item>();

                foreach (Item a in mDoc.TheActionList)
                {
                    if (a is ItemMissing)
                    {
                        continue;
                    }

                    if (a is ActionCopyMoveRename i1)
                    {
                        if (i1.From.RemoveExtension(true).StartsWith(i2.From.RemoveExtension(true), StringComparison.Ordinal))
                        {
                            toRemove.Add(i1);
                        }
                    }
                    else if (a is Item ad)
                    {
                        if (ad.Episode?.AppropriateEpNum == i2.Episode?.AppropriateEpNum &&
                            ad.Episode?.AppropriateSeasonNumber == i2.Episode?.AppropriateSeasonNumber)
                        {
                            toRemove.Add(a);
                        }
                    }
                }

                //Remove all similar items
                mDoc.TheActionList.Remove(toRemove);
            }

            FillActionList();
            RefreshWTW(false, false);
        }
*/

        private void folderMonitorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            BulkAddManager bam = new BulkAddManager(mDoc);
            //FolderMonitor fm = new FolderMonitor(mDoc, bam);
            //fm.ShowDialog();
            FillMyShows();
        }

        private void bnActionWhichSearch_Click(object sender, EventArgs e) => ChooseSiteMenu(0);

/*
        private void lvAction_MouseClick(object sender, [NotNull] MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            // build the right click menu for the _selected_ items, and types of items
            LvResults lvr = new LvResults(lvAction, false);

            if (lvr.Count == 0)
            {
                return; // nothing selected
            }

            Point pt = lvAction.PointToScreen(new Point(e.X, e.Y));

            showRightClickMenu.Items.Clear();

            // Action related items
            if (lvr.Count > lvr.Missing.Count) // not just missing selected
            {
                AddRcMenuItem("Action Selected", RightClickCommands.kActionAction);
            }

            AddRcMenuItem("Ignore Selected", RightClickCommands.kActionIgnore);
            AddRcMenuItem("Ignore Entire Season", RightClickCommands.kActionIgnoreSeason);
            AddRcMenuItem("Remove Selected", RightClickCommands.kActionDelete);

            if (lvr.Count == lvr.Missing.Count) // only missing items selected?
            {
                showRightClickMenu.Items.Add(new ToolStripSeparator());

                ToolStripMenuItem tsi = new ToolStripMenuItem("Search") { Tag = (int)RightClickCommands.kBtSearchFor };
                for (int i = 0; i < TVDoc.GetSearchers().Count(); i++)
                {
                    string name = TVDoc.GetSearchers().Name(i);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        ToolStripMenuItem tssi = new ToolStripMenuItem(name)
                        { Tag = (int)RightClickCommands.kSearchForBase + i };

                        int i1 = i;
                        tssi.Click += (s, ev) => { SearchFor(i1); };
                        tsi.DropDownItems.Add(tssi);
                    }
                }
                showRightClickMenu.Items.Add(tsi);

                if (lvr.Count == 1) // only one selected
                {
                    AddRcMenuItem("Browse For...", RightClickCommands.kActionBrowseForFile);
                }
            }

            if (lvr.CopyMove.Count > 0 || lvr.DownloadTorrents.Count > 0)
            {
                showRightClickMenu.Items.Add(new ToolStripSeparator());
                AddRcMenuItem("Revert to Missing", RightClickCommands.kActionRevert);
            }

            MenuGuideAndTvdb(true);
            MenuFolders(lvr);

            showRightClickMenu.Show(pt);
        }
*/

/*
        private void AddRcMenuItem(string name, RightClickCommands command)
        {
            ToolStripMenuItem tsi = new ToolStripMenuItem(name) { Tag = (int)command };
            showRightClickMenu.Items.Add(tsi);
        }
*/

/*
        private void lvAction_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateSearchButtons();

            LvResults lvr = new LvResults(lvAction, false);

            if (lvr.Count == 0)
            {
                // disable everything
                bnActionBTSearch.Enabled = false;
                return;
            }

            bnActionBTSearch.Enabled = lvr.SaveImages.Count <= 0;

            mLastShowsClicked = null;
            mLastEpClicked = null;
            mLastSeasonClicked = null;
            mLastActionsClicked = null;

            showRightClickMenu.Items.Clear();
            mFoldersToOpen = new List<string>();
            mLastFl = new List<FileInfo>();

            mLastActionsClicked = new ItemList();

            foreach (Item ai in lvr.FlatList)
            {
                mLastActionsClicked.Add(ai);
            }

            if (lvr.Count != 1 || lvAction.FocusedItem?.Tag is null)
            {
                return;
            }

            if (!(lvAction.FocusedItem.Tag is Item action))
            {
                return;
            }

            mLastEpClicked = action.Episode;
            if (action.Episode != null)
            {
                mLastSeasonClicked = action.Episode.AppropriateSeason;
                mLastShowsClicked = new List<ShowItem> { action.Episode.Show };
            }
            else
            {
                mLastSeasonClicked = null;
                mLastShowsClicked = null;
            }

            if (mLastEpClicked != null && TVSettings.Instance.AutoSelectShowInMyShows)
            {
                GotoEpguideFor(mLastEpClicked, false);
            }
        }
*/

        private void ActionDeleteSelected()
        {
            //ListView.SelectedListViewItemCollection sel = lvAction.SelectedItems;
            //foreach (ListViewItem lvi in sel)
            {
                //mDoc.TheActionList.Remove((Item)lvi.Tag);
            }

            FillActionList();
        }

/*
        private void lvAction_KeyDown(object sender, [NotNull] KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                ActionDeleteSelected();
            }
        }
*/

        private void cbActionIgnore_Click(object sender, EventArgs e) => IgnoreSelected();

/*        private void UpdateActionCheckboxes()
        {
            if (internalCheckChange)
            {
                return;
            }

            LvResults all = new LvResults(lvAction, LvResults.WhichResults.all);
            LvResults chk = new LvResults(lvAction, LvResults.WhichResults.Checked);

            SetCheckbox(cbRename, all.Rename, chk.Rename);
            SetCheckbox(cbCopyMove, all.CopyMove, chk.CopyMove);
            SetCheckbox(cbDeleteFiles, all.Deletes, chk.Deletes);
            SetCheckbox(cbSaveImages, all.SaveImages, chk.SaveImages);
            SetCheckbox(cbWriteMetadata, all.WriteMetadatas, chk.WriteMetadatas);
            SetCheckbox(cbModifyMetadata, all.ModifyMetadatas, chk.ModifyMetadatas);
            SetCheckbox(cbDownload, all.DownloadTorrents, chk.DownloadTorrents);

            int total1 = all.FlatList.Count - all.Downloading.Count - all.Missing.Count;

            int total2 = chk.FlatList.Count - chk.Downloading.Count - chk.Missing.Count;

            if (total2 == 0)
            {
                cbAll.CheckState = CheckState.Unchecked;
            }
            else
            {
                cbAll.CheckState = total2 == total1 ? CheckState.Checked : CheckState.Indeterminate;
            }
        }*/

/*        private static void SetCheckbox([NotNull] CheckBox box, IEnumerable<Item> all, [NotNull] IEnumerable<Item> chk)
        {
            IEnumerable<Item> enumerable = chk.ToList();
            if (!enumerable.Any())
            {
                box.CheckState = CheckState.Unchecked;
            }
            else
            {
                box.CheckState = enumerable.Count() == all.Count()
                    ? CheckState.Checked
                    : CheckState.Indeterminate;
            }
        }*/

/*        private void cbActionAllNone_Click(object sender, EventArgs e)
        {
            CheckState cs = cbAll.CheckState;
            if (cs == CheckState.Indeterminate)
            {
                cbAll.CheckState = CheckState.Unchecked;
                cs = CheckState.Unchecked;
            }

            internalCheckChange = true;
            foreach (ListViewItem lvi in lvAction.Items)
            {
                lvi.Checked = cs == CheckState.Checked;
            }

            internalCheckChange = false;
            UpdateActionCheckboxes();
        }

        private void cbDeletes_Click(object sender, EventArgs e)
        {
            CheckState cs = cbDeleteFiles.CheckState;
            if (cs == CheckState.Indeterminate)
            {
                cbDeleteFiles.CheckState = CheckState.Unchecked;
                cs = CheckState.Unchecked;
            }

            internalCheckChange = true;
            foreach (ListViewItem lvi in lvAction.Items)
            {
                Item i = (Item)lvi.Tag;
                if (i is ActionDelete)
                {
                    lvi.Checked = cs == CheckState.Checked;
                }
            }

            internalCheckChange = false;
            UpdateActionCheckboxes();
        }

        private void cbActionRename_Click(object sender, EventArgs e)
        {
            CheckState cs = cbRename.CheckState;
            if (cs == CheckState.Indeterminate)
            {
                cbRename.CheckState = CheckState.Unchecked;
                cs = CheckState.Unchecked;
            }

            internalCheckChange = true;
            foreach (ListViewItem lvi in lvAction.Items)
            {
                Item i = (Item)lvi.Tag;
                if (i is ActionCopyMoveRename rename && rename.Operation == ActionCopyMoveRename.Op.rename)
                {
                    lvi.Checked = cs == CheckState.Checked;
                }
            }

            internalCheckChange = false;
            UpdateActionCheckboxes();
        }

        private void cbActionCopyMove_Click(object sender, EventArgs e)
        {
            CheckState cs = cbCopyMove.CheckState;
            if (cs == CheckState.Indeterminate)
            {
                cbCopyMove.CheckState = CheckState.Unchecked;
                cs = CheckState.Unchecked;
            }

            internalCheckChange = true;
            foreach (ListViewItem lvi in lvAction.Items)
            {
                Item i = (Item)lvi.Tag;
                if (i is ActionCopyMoveRename copymove && copymove.Operation != ActionCopyMoveRename.Op.rename)
                {
                    lvi.Checked = cs == CheckState.Checked;
                }
            }

            internalCheckChange = false;
            UpdateActionCheckboxes();
        }

        private void cbActionNFO_Click(object sender, EventArgs e)
        {
            CheckState cs = cbWriteMetadata.CheckState;
            if (cs == CheckState.Indeterminate)
            {
                cbWriteMetadata.CheckState = CheckState.Unchecked;
                cs = CheckState.Unchecked;
            }

            internalCheckChange = true;
            foreach (ListViewItem lvi in lvAction.Items)
            {
                Item i = (Item)lvi.Tag;
                if (i is ActionWriteMetadata)
                {
                    lvi.Checked = cs == CheckState.Checked;
                }
            }

            internalCheckChange = false;
            UpdateActionCheckboxes();
        }

        private void cbActionModifyMetaData_Click(object sender, EventArgs e)
        {
            CheckState cs = cbModifyMetadata.CheckState;
            if (cs == CheckState.Indeterminate)
            {
                cbModifyMetadata.CheckState = CheckState.Unchecked;
                cs = CheckState.Unchecked;
            }

            internalCheckChange = true;
            foreach (ListViewItem lvi in lvAction.Items)
            {
                Item i = (Item)lvi.Tag;
                if (i is ActionFileMetaData)
                {
                    lvi.Checked = cs == CheckState.Checked;
                }
            }

            internalCheckChange = false;
            UpdateActionCheckboxes();
        }

        private void cbActionRSS_Click(object sender, EventArgs e)
        {
            CheckState cs = cbDownload.CheckState;
            if (cs == CheckState.Indeterminate)
            {
                cbDownload.CheckState = CheckState.Unchecked;
                cs = CheckState.Unchecked;
            }

            internalCheckChange = true;
            foreach (ListViewItem lvi in lvAction.Items)
            {
                Item i = (Item)lvi.Tag;
                if (i is ActionTDownload)
                {
                    lvi.Checked = cs == CheckState.Checked;
                }
            }

            internalCheckChange = false;
            UpdateActionCheckboxes();
        }

        private void cbActionDownloads_Click(object sender, EventArgs e)
        {
            CheckState cs = cbSaveImages.CheckState;
            if (cs == CheckState.Indeterminate)
            {
                cbSaveImages.CheckState = CheckState.Unchecked;
                cs = CheckState.Unchecked;
            }

            internalCheckChange = true;
            foreach (ListViewItem lvi in lvAction.Items)
            {
                Item i = (Item)lvi.Tag;
                if (i is ActionDownloadImage)
                {
                    lvi.Checked = cs == CheckState.Checked;
                }
            }

            internalCheckChange = false;
            UpdateActionCheckboxes();
        }*/

/*
        private void lvAction_ItemCheck(object sender, [NotNull] ItemCheckEventArgs e)
        {
            if (e.Index < 0 || e.Index > lvAction.Items.Count)
            {
                return;
            }

            Item action = (Item)lvAction.Items[e.Index].Tag;
            if (action != null && (action is ItemMissing || action is ItemDownloading))
            {
                e.NewValue = CheckState.Unchecked;
            }
        }
*/

        private void bnActionOptions_Click(object sender, EventArgs e) => DoPrefs(true);

/*
        private void lvAction_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            // double-click on an item will search for missing, do nothing (for now) for anything else
            foreach (ItemMissing miss in new LvResults(lvAction, false).Missing)
            {
                if (miss.Episode != null)
                {
                    TVDoc.SearchForEpisode(miss.Episode);
                }
            }
        }
*/

/*
        private void bnActionBTSearch_Click(object sender, EventArgs e)
        {
            LvResults lvr = new LvResults(lvAction, false);

            if (lvr.Count == 0)
            {
                return;
            }

            foreach (Item i in lvr.FlatList)
            {
                if (i?.Episode != null)
                {
                    TVDoc.SearchForEpisode(i.Episode);
                }
            }
        }
*/

        private void bnRemoveSel_Click(object sender, EventArgs e) => ActionDeleteSelected();

        private void IgnoreSelected()
        {
            //LvResults lvr = new LvResults(lvAction, false);
            bool added = false;
//            foreach (Item action in lvr.FlatList)
            {
  //              IgnoreItem ii = action.Ignore;
                //if (ii != null)
                {
                  //  TVSettings.Instance.Ignore.Add(ii);
                    added = true;
                }
            }

            if (added)
            {
                mDoc.SetDirty();
                mDoc.RemoveIgnored();
                FillActionList();
            }
        }

        private void ignoreListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //IgnoreEdit ie = new IgnoreEdit(mDoc);
            //ie.ShowDialog();
        }

        private async void showSummaryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //UseWaitCursor = true;
            //ShowSummary f = new ShowSummary(mDoc);
            //await Task.Run(() => f.GenerateData());
            //f.PopulateGrid();
            //UseWaitCursor = false;
            //f.Show();
        }

        //private void lvAction_ItemChecked(object sender, ItemCheckedEventArgs e) => UpdateActionCheckboxes();

/*
        private void bnHideHTMLPanel_Click(object sender, EventArgs e)
        {
            if (splitContainer1.Panel2Collapsed)
            {
                splitContainer1.Panel2Collapsed = false;
                bnHideHTMLPanel.ImageKey = "FillRight.bmp";
            }
            else
            {
                splitContainer1.Panel2Collapsed = true;
                bnHideHTMLPanel.ImageKey = "FillLeft.bmp";
            }
        }
*/

        private void bnActionRecentCheck_Click(object sender, EventArgs e) =>
            UiScan(null, false, TVSettings.ScanType.Recent);

        private void btnActionQuickScan_Click(object sender, EventArgs e) =>
            UiScan(null, false, TVSettings.ScanType.Quick);

        private void btnFilter_Click(object sender, EventArgs e)
        {
            //Filters filters = new Filters(mDoc);
            //var res = filters.ShowDialog();
            //if (res == .OK)
            //{
                //FillMyShows();
            //}
        }

/*
        private void lvAction_DragDrop(object sender, [NotNull] DragEventArgs e)
        {
            // Get a list of filenames being dragged
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop, false);

            // Establish item in list being dragged to, and exit if no item matched
            Point localPoint = lvAction.PointToClient(new Point(e.X, e.Y));
            ListViewItem lvi = lvAction.GetItemAt(localPoint.X, localPoint.Y);
            if (lvi is null)
            {
                return;
            }

            // Check at least one file was being dragged, and that dragged-to item is a "Missing Item" item.
            if (files.Length <= 0 || !(lvi.Tag is ItemMissing mi))
            {
                return;
            }

            // Only want the first file if multiple files were dragged across.
            ManuallyAddFileForItem(mi, files[0]);
        }
*/

/*
        private void lvAction_DragEnter(object sender, [NotNull] DragEventArgs e)
        {
            e.Effects = DragDropEffects.All;
            Point localPoint = lvAction.PointToClient(e.GetPosition(lvAction));
            ListViewItem lvi = lvAction.GetItemAt(localPoint.X, localPoint.Y);
            // If we're not dragging over a "ItemMissing" entry, or if we're not dragging a list of files, then change the DragDropEffect
            if (!(lvi?.Tag is ItemMissing) || !e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects =DragDropEffects.None;
            }
        }
*/

        private void filterTextBox_TextChanged(object sender, TextChangedEventArgs e) => FillMyShows();

        private void visitSupportForumToolStripMenuItem_Click(object sender, EventArgs e)
            => Helpers.SysOpen("https://groups.google.com/forum/#!forum/tvrename");

        public void Quit() => Close();

        private async void checkForNewVersionToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Dispatcher uiDisp = Dispatcher.CurrentDispatcher;

            Task<Release> tuv = VersionUpdater.CheckForUpdatesAsync();
            Release result = await tuv.ConfigureAwait(false);

            uiDisp.Invoke(() => NotifyUpdates(result, true));
        }

        private void NotifyUpdates([CanBeNull] Release update, bool showNoUpdateRequiredDialog, bool inSilentMode = false)
        {
            if (update is null)
            {
                //btnUpdateAvailable.Visible = false;
                if (showNoUpdateRequiredDialog && !inSilentMode)
                {
                    MessageBox.Show(@"There is no update available please try again later.", @"No update available",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            Logger.Warn(update.LogMessage());

            if (inSilentMode || Debugger.IsAttached)
            {
                return;
            }

            //UpdateNotification unForm = new UpdateNotification(update);
            //unForm.ShowDialog();
            //if (unForm.MessageBoxResult == MessageBoxResult.Abort)
            {
                Logger.Info("Downloading New Release and Quiting");
                //We need to quit!
                //Close();
            }
            //btnUpdateAvailable.Visible = true;
        }

        private void duplicateFinderLOGToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<PossibleDuplicateEpisode> x = Beta.FindDoubleEps(mDoc);
            //DupEpFinder form = new DupEpFinder(x, mDoc, this);
            //form.ShowDialog();
        }

        private async void btnUpdateAvailable_Click(object sender, EventArgs e)
        {
            //btnUpdateAvailable.Visible = false;

            //Dispatcher uiDisp = Dispatcher.CurrentDispatcher;

            Task<Release> tuv = VersionUpdater.CheckForUpdatesAsync();
            Release result = await tuv.ConfigureAwait(false);

            //uiDisp.Invoke(() => NotifyUpdates(result, true));
        }

        private void tmrPeriodicScan_Tick(object sender, EventArgs e) => RunAutoScan("Periodic Scan");

        private void RunAutoScan(string scanType)
        {
            //We only wish to do a scan now if we are not already undertaking one
            if (mDoc.AutoScanCanRun())
            {
                Logger.Info("*******************************");
                Logger.Info(scanType + " fired");
                UiScan(null, true, TVSettings.Instance.MonitoredFoldersScanType);
                ProcessAll();
                Logger.Info(scanType + " complete");
            }
            else
            {
                Logger.Info(scanType + " cancelled as the system is already busy");
            }
        }

        private void timezoneInconsistencyLOGToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //Show Log Pane
            logToolStripMenuItem_Click(sender, e);

            Task.Run(() => {
                TimeZoneTracker results = new TimeZoneTracker();
                foreach (ShowItem si in mDoc.Library.GetShowItems())
                {
                    SeriesInfo ser = si.TheSeries();

                    if (ser != null)
                    {
                        //si.ShowTimeZone = TimeZone.TimeZoneForNetwork(ser.getNetwork());

                        results.Add(ser.Network, si.ShowTimeZone, si.ShowName);
                    }
                }
                Logger.Info(results.PrintVersion());
            });
        }

        private class TimeZoneTracker
        {
            private readonly Dictionary<string, Dictionary<string, List<string>>> tzt =
                new Dictionary<string, Dictionary<string, List<string>>>();

            internal void Add([NotNull] string network, [NotNull] string timezone, string show)
            {
                if (!tzt.ContainsKey(network))
                {
                    tzt.Add(network, new Dictionary<string, List<string>>());
                }

                Dictionary<string, List<string>> snet = tzt[network];

                if (!snet.ContainsKey(timezone))
                {
                    snet.Add(timezone, new List<string>());
                }

                List<string> snettz = snet[timezone];

                snettz.Add(show);
            }

            [NotNull]
            internal string PrintVersion()
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("***********************************");
                sb.AppendLine("****Timezone Comparison       *****");
                sb.AppendLine("***********************************");
                foreach (KeyValuePair<string, Dictionary<string, List<string>>> kvp in tzt)
                {
                    foreach (KeyValuePair<string, List<string>> kvp2 in kvp.Value)
                    {
                        sb.AppendLine($"{kvp.Key,-30}{kvp2.Key,-30}{string.Join(",", kvp2.Value)}");
                    }
                }

                return sb.ToString();
            }
        }

        private void exportToolStripMenuItem_Click(object sender, EventArgs e)
        {
            mDoc.RunExporters();
        }

        private void logToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //LogViewer form = new LogViewer();
            //form.Show();
        }

        private void episodeFileQualitySummaryLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //Show Log Pane
            logToolStripMenuItem_Click(sender, e);

            Task.Run(() => {
                Beta.LogShowEpisodeSizes(mDoc);
            });
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //LicenceInfoForm form = new LicenceInfoForm();
            //form.ShowDialog();
        }

/*
        private void LvAction_ColumnClick(object sender, [NotNull] ColumnClickEventArgs e)
        {
            int col = e.Column;

            switch (col)
            {
                case 3:
                    lvAction.ListViewItemSorter = new DateSorterScan(col);
                    break;
                case 1:
                case 2:
                    lvAction.ListViewItemSorter = new NumberAsTextSorter(col);
                    break;
                default:
                    lvAction.ListViewItemSorter = new TextSorter(col);
                    break;
            }

            lvAction.Sort();
            lvAction.Refresh();
        }
*/

        private void QuickRenameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //QuickRename form = new QuickRename(mDoc, this);
            //form.Show();
        }

        public void FocusOnScanResults()
        {
            //tabControl1.Select(tbAllInOne);
        }

        private void MyShowTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            switch (e.NewValue)
            {
                case null:
                    viewMyShowsViewModel.SelectedShowOrSeason = null;
                    break;
                case ShowModel sm:
                    viewMyShowsViewModel.SelectedShowOrSeason = sm.Show;
                    break;
                case Season sn:
                    viewMyShowsViewModel.SelectedShowOrSeason = sn;
                    break;
                default:
                    throw new ArgumentException();
            }
        }

        private void ShowFilters_OnExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            ShowFilterViewModel filtersvm = new ViewModel.ShowFilterViewModel(mDoc.Library)
            {
                SelectedShowRating = viewMyShowsViewModel.Filter.ShowRating,
                SelectedNetwork = viewMyShowsViewModel.Filter.ShowNetwork,
                SelectedShowStatus = viewMyShowsViewModel.Filter.ShowStatus,
                SelectedShowName = viewMyShowsViewModel.Filter.ShowName,
                SelectedGenres = viewMyShowsViewModel.Filter.Genres
            };
            Filters dialog = new Filters {DataContext = filtersvm };
            dialog.ShowDialog();

            if (dialog.DialogResult.HasValue && dialog.DialogResult.Value)
            {
                ShowFilter newFilter = new ShowFilter
                {
                    ShowName = filtersvm.SelectedShowName,
                    ShowNetwork = filtersvm.SelectedNetwork,
                    ShowRating = filtersvm.SelectedShowRating,
                    ShowStatus = filtersvm.SelectedShowStatus,
                    Genres = filtersvm.SelectedGenres
                };

                viewMyShowsViewModel.Filter = newFilter;
            }

            //else do not update the viewmodel with the new filters
        }

        private void ExitCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        private void ExitCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}

