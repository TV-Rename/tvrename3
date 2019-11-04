// 
// Main website for TVRename is http://tvrename.com
// 
// Source code available at https://github.com/TV-Rename/tvrename
// 
// Copyright (c) TV Rename. This code is released under GPLv3 https://github.com/TV-Rename/tvrename/blob/master/LICENSE.md
// 

using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace TVRename
{
    public class ShowFilter
    {
        public List<string> Genres { get; set; } = new List<string>();
        public string ShowName { get; set; } 
        public string ShowStatus { get; set; }
        public string ShowNetwork { get; set; }
        public string ShowRating { get; set; }

        public bool Filter([NotNull] ShowItem show)
        {
            SeriesInfo seriesInfo = show.TheSeries();

            //Filter on show name
            bool isNameOk = (ShowName is null) || show.ShowName.Contains(ShowName, StringComparison.OrdinalIgnoreCase);

            //Filter on show status
            bool isStatusOk = (ShowStatus is null) || show.ShowStatus.Equals(ShowStatus);

            //Filter on show network
            bool isNetworkOk = (ShowNetwork is null) || seriesInfo is null || seriesInfo.Network.Equals(ShowNetwork);

            //Filter on show rating
            bool isRatingOk = ShowRating is null || seriesInfo is null || seriesInfo.ContentRating.Equals(ShowRating);

            //Filter on show genres
            bool areGenresIgnored = (Genres?.Count == 0);
            bool doAnyGenresMatch = FindMatchingGenres(show);

            return isNameOk && isStatusOk && isNetworkOk && isRatingOk && (areGenresIgnored || doAnyGenresMatch);
        }

        private bool FindMatchingGenres([NotNull] ShowItem show)
        {
            return Genres is null || show.Genres.Any(showGenre => Genres.Contains(showGenre));
        }
    }
}
