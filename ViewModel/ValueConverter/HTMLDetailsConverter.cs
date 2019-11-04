using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;

namespace TVRename.Converters
{
    [ValueConversion(typeof(ISelectableShowPart), typeof(string))]
    public class HTMLDetailsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            ISelectableShowPart s = (ISelectableShowPart)value;
            switch (s)
            {
                case null:
                    return ShowHtmlHelper.CreateOldPage("Not downloaded, or not available");
                case ShowItem si:
                    if (TVSettings.Instance.OfflineMode || TVSettings.Instance.ShowBasicShowDetails)
                    {
                        return ShowHtmlHelper.CreateOldPage(si.GetShowHtmlOverviewOffline());
                    }
                    else
                    {
                        return si.GetShowHtmlOverview(true);
                    }
                case Season sn:
                    if (TVSettings.Instance.OfflineMode || TVSettings.Instance.ShowBasicShowDetails)
                    {
                        return ShowHtmlHelper.CreateOldPage(sn.TheSeries.Show.GetSeasonHtmlOverviewOffline(sn));
                    }
                    else
                    {
                        return sn.TheSeries.Show.GetSeasonHtmlOverview(sn, true);
                    }
            }

            throw new InvalidEnumArgumentException();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}