// 
// Main website for TVRename is http://tvrename.com
// 
// Source code available at https://github.com/TV-Rename/tvrename
// 
// Copyright (c) TV Rename. This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
// 

using Alphaleonis.Win32.Filesystem;
using JetBrains.Annotations;

namespace TVRename
{
    // ReSharper disable once InconsistentNaming
    class RecentWPLExporter : RecentExporter
    {
        public RecentWPLExporter(TVDoc doc) : base(doc)
        {
        }

        public override bool Active() => TVSettings.Instance.ExportRecentWPL;
        protected override string Location() => TVSettings.Instance.ExportRecentWPLTo;

        [NotNull]
        protected override string GenerateHeader()
        {
            return $"<?wpl version=\"1.0\"?>\r\n<smil>\r\n    <head>\r\n        <meta name=\"Generator\" content=\"TV Rename -- {Helpers.DisplayVersion}\"/>\r\n        <title>Recent ASX Export</title>\r\n    </head>\r\n    <body>\r\n        <seq>";
        }

        [NotNull]
        protected override string GenerateRecord(ProcessedEpisode ep, [NotNull] FileInfo file, string name, int length)
        {
            string filen = System.Security.SecurityElement.Escape(file.UrlPathFullName());
            return $"             <media src=\"{filen}\"/>";
        }

        [NotNull]
        protected override string GenerateFooter()
        {
            return "         </seq>\r\n    </body>\r\n</smil>";
        }
    }
}
