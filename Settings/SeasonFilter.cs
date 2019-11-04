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
    public class SeasonFilter
    {
        public bool HideIgnoredSeasons { get; set; }

        public bool Filter(ShowItem si, [NotNull] Season sea)
        {
            if (sea.SeasonNumber == 0 && TVSettings.Instance.IgnoreAllSpecials)
            {
                return true;
            }

            return !HideIgnoredSeasons || !si.IgnoreSeasons.Contains(sea.SeasonNumber);
        }
    }
}
