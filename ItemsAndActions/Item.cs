// 
// Main website for TVRename is http://tvrename.com
// 
// Source code available at https://github.com/TV-Rename/tvrename
// 
// Copyright (c) TV Rename. This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
// 

using JetBrains.Annotations;

namespace TVRename
{
    using System.Windows.Forms;

    public abstract class Item // something shown in the list on the Scan tab (not always an Action)
    {
        public abstract string TargetFolder { get; } // return a list of folders for right-click menu
        public abstract string ScanListViewGroup { get; } // which group name for the listview
        public abstract int IconNumber { get; } // which icon number to use in "ilIcons" (UI.cs). -1 for none
        public abstract IgnoreItem Ignore { get; } // what to add to the ignore list / compare against the ignore list
        public ProcessedEpisode Episode { get; protected set; } // associated episode
        public abstract int Compare(Item o); // for sorting items in scan list (ActionItemSorter)
        public abstract bool SameAs(Item o); // are we the same thing as that other one?

        [CanBeNull]
        protected static IgnoreItem GenerateIgnore([CanBeNull] string file) => string.IsNullOrEmpty(file) ? null : new IgnoreItem(file);

        [NotNull]
        protected virtual string SeriesName => Episode?.TheSeries?.Name ?? string.Empty;

        [NotNull]
        protected virtual string SeasonNumber =>
              Episode is null ? string.Empty
            : Episode.AppropriateSeasonNumber == 0 ? "Special"
            : Episode.AppropriateSeasonNumber.ToString();

        [NotNull]
        protected virtual string EpisodeNumber => Episode?.NumsAsString() ?? string.Empty;
        [NotNull]
        protected virtual string AirDate => Episode?.GetAirDateDt(true).PrettyPrint() ?? string.Empty;
        protected abstract string DestinationFolder { get; }
        protected abstract string DestinationFile { get; }
        [NotNull]
        protected virtual string SourceDetails => string.Empty;
        protected virtual bool InError => false;
        public string ErrorText { get; protected set; } // Human-readable error message, for when Error is true
    }
}
