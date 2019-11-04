using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace TVRename
{
    public class ShowModel
    {
        public ShowItem Show { get; set; }
        public ObservableCollection<Season> VisibleSeasons { get; }=new ObservableCollection<Season>();
    }

    public class MyShowsViewModel : INotifyPropertyChanged
    {
        private ISelectableShowPart selectedShowOrSeason;
        private string filterText;
        private ShowFilter filter;
        private ObservableCollection<ShowModel> visibleShows;
        private readonly ShowLibrary library;

        public MyShowsViewModel(ShowLibrary lib)
        {
            library = lib;
        }

        public ISelectableShowPart SelectedShowOrSeason
        {
            get => selectedShowOrSeason;
            set
            {
                selectedShowOrSeason = value;
                NotifyPropertyChanged();
            }
        }

        public string FilterText
        {
            get => filterText;
            set
            {
                filterText = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(VisibleShows));
            }
        }

        public ObservableCollection<ShowModel> VisibleShows
        {
            get
            {
                ObservableCollection<ShowModel> newCollection = new ObservableCollection<ShowModel>();

                List<ShowItem> sil = library.GetShowItems();

                sil.Sort((a, b) =>
                {
                    string aname = new ShowNameConverter().Convert(a);
                    string bname = new ShowNameConverter().Convert(b);
                    return string.Compare(aname, bname, StringComparison.OrdinalIgnoreCase);
                });

                foreach (ShowItem si in sil)
                {
                    bool matchesAdvFilters = (filter is null) || (filter.Filter(si));
                    bool matchesSimpleFilter = (string.IsNullOrEmpty(filterText) || si.NameMatchFilters(filterText));

                    if (!(matchesSimpleFilter & matchesAdvFilters)) continue;

                    ShowModel s = new ShowModel {Show = si};

                    List<int> theKeys = si.DvdOrder
                        ? new List<int>(si.Series.DvdSeasons.Keys)
                        : new List<int>(si.Series.AiredSeasons.Keys);

                    theKeys.Sort();

                    SeasonFilter sf = TVSettings.Instance.SeasonFilter;
                    foreach (int snum in theKeys)
                    {
                        Season sn = si.DvdOrder ? si.Series.DvdSeasons[snum] : si.Series.AiredSeasons[snum];

                        //Ignore the season if it is filtered out
                        if (!sf.Filter(si, sn))
                        {
                            continue;
                        }

                        if (snum == 0 && TVSettings.Instance.IgnoreAllSpecials)
                        {
                            continue;
                        }

                        s.VisibleSeasons.Add(sn);
                    }

                    newCollection.Add(s);
                }


//                        if (expanded.Contains(si))
//                        {
//                            tvn.Expand();
//                        }

                visibleShows = newCollection;
                return visibleShows;
            }
            set => throw new NotImplementedException();
        }

        public ShowFilter Filter
        {
            get => filter;
            set
            {
                filter = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(VisibleShows));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }



        private void FillEpGuideHtml()
        {
            //            if (MyShowTree.Nodes.Count == 0)
            //            {
            //                ShowQuickStartGuide();
            //            }
            //            else
            //            {
            //                TreeViewItem n = MyShowTree.SelectedNode;
            //                FillEpGuideHtml(n);
            //            }
        }

        /*
        private void FillEpGuideHtml([CanBeNull] TreeViewItem n)
        {
            if (n is null)
            {
                FillEpGuideHtml(null, -1);
                return;
            }

            if (n.Tag is ProcessedEpisode pe)
            {
                FillEpGuideHtml(pe.Show, pe.AppropriateSeasonNumber);
                return;
            }

            Season seas = TreeViewItemToSeason(n);
            if (seas != null)
            {
                // we have a TVDB season, but need to find the equivalent one in our local processed episode collection
                if (seas.Episodes.Count > 0)
                {
                    int tvdbcode = seas.TheSeries.TvdbCode;
                    foreach (ShowItem si in mDoc.Library.Values.Where(si => si.TvdbCode == tvdbcode))
                    {
                        FillEpGuideHtml(si, seas.SeasonNumber);
                        return;
                    }
                }

                FillEpGuideHtml(null, -1);
                return;
            }

            FillEpGuideHtml(TreeViewItemToShowItem(n), -1);
        }
*/

    }
}

