// 
// Main website for TVRename is http://tvrename.com
// 
// Source code available at https://github.com/TV-Rename/tvrename
// 
// Copyright (c) TV Rename. This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
// 

using System.Collections.Generic;
using Alphaleonis.Win32.Filesystem;

namespace TVRename
{
    // ReSharper disable once InconsistentNaming
    public interface iTVSource
    {
        void Setup(ShowLibrary d, FileInfo loadFrom, FileInfo cacheFile, CommandLineArgs args);
        bool Connect(bool showErrorMsgBox);
        void SaveCache();

        bool EnsureUpdated(SeriesSpecifier s, bool bannersToo);
        bool GetUpdates(bool showErrorMsgBox);
        void UpdatesDoneOk();

        SeriesInfo GetSeries(ShowLibrary d, string showName, bool showErrorMsgBox);
        SeriesInfo GetSeries(int id);
        bool HasSeries(int id);

        void Tidy(ICollection<ShowItem> libraryValues);

        void ForgetEverything();
        void ForgetShow(int id);
        void ForgetShow(ShowLibrary d, int id, bool makePlaceholder,bool useCustomLanguage,string langCode);
    }
}
