using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;

namespace TVRename.Converters

{
    [ValueConversion(typeof(ISelectableShowPart), typeof(string))]
    public class HTMLImageDetailsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            ISelectableShowPart s = (ISelectableShowPart)value;
            switch (s)
            {
                case null:
                    return ShowHtmlHelper.CreateOldPage("Not downloaded, or not available");
                case ShowItem si:
                    return ShowHtmlHelper.CreateOldPage(si.GetShowImagesHtmlOverview());
                case Season sn:
                    return ShowHtmlHelper.CreateOldPage(sn.TheSeries.Show.GetSeasonImagesHtmlOverview(sn));
            }

            throw new InvalidEnumArgumentException();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}