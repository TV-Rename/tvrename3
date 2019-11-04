// 
// Main website for TVRename is http://tvrename.com
// 
// Source code available at https://github.com/TV-Rename/tvrename
// 
// Copyright (c) TV Rename. This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
// 

using Alphaleonis.Win32.Filesystem;
using System;
using JetBrains.Annotations;

namespace TVRename
{
    public class ItemDownloading : Item
    {
        private readonly IDownloadInformation entry;
        public readonly string DesiredLocationNoExt;

        [CanBeNull]
        public override IgnoreItem Ignore => GenerateIgnore(DesiredLocationNoExt);
        [NotNull]
        public override string ScanListViewGroup => "lvgDownloading";

        [CanBeNull]
        protected override string DestinationFolder => TargetFolder;
        protected override string DestinationFile => entry.FileIdentifier;
        protected override string SourceDetails => entry.RemainingText;

        public override int IconNumber { get; }
        [CanBeNull]
        public override string TargetFolder => string.IsNullOrEmpty(entry.Destination) ? null : new FileInfo(entry.Destination).DirectoryName;

        public ItemDownloading(IDownloadInformation dl, ProcessedEpisode pe, string desiredLocationNoExt, DownloadingFinder.DownloadApp tApp)
        {
            Episode = pe;
            DesiredLocationNoExt = desiredLocationNoExt;
            entry = dl;
            IconNumber = (tApp == DownloadingFinder.DownloadApp.uTorrent) ? 2 :
                         (tApp == DownloadingFinder.DownloadApp.SABnzbd)  ? 8 :
                         (tApp == DownloadingFinder.DownloadApp.qBitTorrent) ? 10 : 0;
        }

        #region Item Members

        public override bool SameAs(Item o)
        {
            return (o is ItemDownloading torrenting) && entry == torrenting.entry;
        }

        public override int Compare(Item o)
        {
            if (!(o is ItemDownloading ut))
            {
                return 0;
            }

            if (Episode is null)
            {
                return 1;
            }

            if (ut.Episode is null)
            {
                return -1;
            }

            return string.Compare((DesiredLocationNoExt), ut.DesiredLocationNoExt, StringComparison.Ordinal);
        }
        #endregion
    }
}
