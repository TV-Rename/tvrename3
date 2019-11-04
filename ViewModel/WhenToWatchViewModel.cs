using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using JetBrains.Annotations;

namespace TVRename
{
    public class WhenToWatchViewModel : INotifyPropertyChanged
    {
        private readonly ShowLibrary lib;

        public WhenToWatchViewModel()
        {
            lib = null;
        }
        public WhenToWatchViewModel(ShowLibrary library)
        {
            lib = library;
        }

        public DateTime SelectedDateTime
        {
            get => selecteDateTime;
            set
            {
                selecteDateTime = value;
                NotifyPropertyChanged();
            }
        }

        public ListCollectionView UpcomingEpisodes
        {
            get
            {
                if (lib is null) return new ListCollectionView(new List<ProcessedEpisode>());

                ListCollectionView collectionView = new ListCollectionView(lib?.GetRecentAndFutureEps(TVSettings.Instance.WTWRecentDays).ToList());
                collectionView.GroupDescriptions.Add(new PropertyGroupDescription("VisibleGroup"));

                return collectionView;
            }
        }
        /*
        
        private void UpdateWtw(DirFilesCache dfc, [NotNull] ProcessedEpisode pe, [NotNull] object lvi)
        {
            lvi.Tag = pe;

            // group 0 = just missed
            //       1 = this week
            //       2 = future / unknown

            DateTime? airdt = pe.GetAirDateDt(true);
            if (airdt is null)
            {
                // TODO: something!
                return;
            }

            DateTime dt = (DateTime)airdt;

            double ttn = dt.Subtract(DateTime.Now).TotalHours;


            if (ttn < 0)
            {
                lvi.Group = lvWhenToWatch.Groups["justPassed"];
            }
            else if (ttn < 7 * 24)
            {
                lvi.Group = lvWhenToWatch.Groups["next7days"];
            }
            else if (!pe.NextToAir)
            {
                lvi.Group = lvWhenToWatch.Groups["later"];
            }
            else
            {
                lvi.Group = lvWhenToWatch.Groups["futureEps"];
            }

            int n = 0;
            lvi.Text = pe.Show.ShowName;
            lvi.SubItems[++n].Text =
                pe.AppropriateSeasonNumber != 0 ? pe.AppropriateSeasonNumber.ToString() : "Special";

            string estr = pe.AppropriateEpNum > 0 ? pe.AppropriateEpNum.ToString() : "";
            if (pe.AppropriateEpNum > 0 && pe.EpNum2 != pe.AppropriateEpNum && pe.EpNum2 > 0)
            {
                estr += "-" + pe.EpNum2;
            }

            lvi.SubItems[++n].Text = estr;
            lvi.SubItems[++n].Text = dt.ToShortDateString();
            lvi.SubItems[++n].Text = dt.ToString("t");
            lvi.SubItems[++n].Text = dt.ToString("ddd");
            lvi.SubItems[++n].Text = pe.HowLong();
            lvi.SubItems[++n].Text = pe.TheSeries.Network;
            lvi.SubItems[++n].Text = pe.Name;

            // icon..

            if (airdt.Value.CompareTo(DateTime.Now) < 0) // has aired
            {
                List<FileInfo> fl = dfc.FindEpOnDisk(pe);
                bool appropriateFileNameFound = !TVSettings.Instance.RenameCheck
                           || !pe.Show.DoRename
                           || fl.All(file => file.Name.StartsWith(TVSettings.Instance.FilenameFriendly(TVSettings.Instance.NamingStyle.NameFor(pe)), StringComparison.OrdinalIgnoreCase));

                if (fl.Count > 0 && appropriateFileNameFound)
                {
                    lvi.ImageIndex = 0;
                }
                else if (pe.Show.DoMissingCheck)
                {
                    lvi.ImageIndex = 1;
                }
                else
                {
                    //We don't use an image in this case
                }
            }
        }
*/

        /*
        private void FillWhenToWatchList()
        {
            

            //lvWhenToWatch.Groups["justPassed"].Header ="Aired in the last " + dd + " day" + (dd == 1 ? "" : "s");

            // try to maintain selections if we can
            //List<ProcessedEpisode> selections = new List<ProcessedEpisode>();
            //foreach (ListViewItem lvi in lvWhenToWatch.SelectedItems)
            {
                //selections.Add((ProcessedEpisode)lvi.Tag);
            }

            //TreeViewItem n = (TreeViewItem)MyShowTree.SelectedItem;
            //Season currentSeas = TreeViewItemToSeason(n);
            //ShowItem currentShowItem = TreeViewItemToShowItem(n);

            //lvWhenToWatch.Items.Clear();

            List<DateTime> bolded = new List<DateTime>();
            DirFilesCache dfc = new DirFilesCache();

            IEnumerable<ProcessedEpisode> recentEps = mDoc.Library.GetRecentAndFutureEps(dd);

            foreach (ProcessedEpisode ei in recentEps)
            {
                DateTime? dt = ei.GetAirDateDt(true);
                if (dt != null)
                {
                    bolded.Add(dt.Value);
                }

                //ListViewItem lvi = new ListViewItem { Text = "" };
                for (int i = 0; i < 8; i++)
                {
                    //lvi.SubItems.Add("");
                }

                //UpdateWtw(dfc, ei, lvi);

                //lvWhenToWatch.Items.Add(lvi);

                foreach (ProcessedEpisode pe in selections)
                {
                    if (!pe.SameAs(ei))
                    {
                        continue;
                    }

                    //lvi.Selected = true;
                    break;
                }
            }

            //lvWhenToWatch.Sort();

            //lvWhenToWatch.EndUpdate();
            //calCalendar.BoldedDates = bolded.ToArray();

            if (currentSeas != null)
            {
                SelectSeason(currentSeas);
            }
            else if (currentShowItem != null)
            {
                SelectShow(currentShowItem);
            }

            //UpdateToolstripWTW();
        }
*/

        private DateTime selecteDateTime;

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }
}