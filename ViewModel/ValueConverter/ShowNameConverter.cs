using System;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using NLog.LayoutRenderers.Wrappers;

namespace TVRename
{
    [ValueConversion(typeof(ShowModel), typeof(string))]
    public class ShowNameConverter :IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            ShowItem si = ((ShowModel)value)?.Show;
            return Convert(si);
        }

        public string Convert(ShowItem si)
        {
            SeriesInfo ser = si?.Series;

            string name = si?.ShowName;

            if (string.IsNullOrEmpty(name))
            {
                if (ser != null)
                {
                    name = ser.Name;
                }
                else
                {
                    name += "-- Unknown : " + si?.TvdbCode + " --";
                }
            }

            if (TVSettings.Instance.PostpendThe && name.StartsWith("The ", StringComparison.Ordinal))
            {
                return name.Substring(4) + ", The";
            }

            return name;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(ShowModel), typeof(Brush))]
    public class ShowColourConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            ShowItem si = ((ShowModel)value)?.Show;
            if (TVSettings.Instance.ShowStatusColors == null || si is null) return Brushes.Black;

            if (TVSettings.Instance.ShowStatusColors.IsShowStatusDefined(si.ShowStatus))
            {
                return new SolidColorBrush(TVSettings.Instance.ShowStatusColors.GetEntry(false, true, si.ShowStatus)?? Colors.Black);
            }

            Color? nodeColor =
                TVSettings.Instance.ShowStatusColors.GetEntry(true, true, si.SeasonsAirStatus.ToString());

            return new SolidColorBrush(nodeColor ?? Colors.Black);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }


    [ValueConversion(typeof(Season), typeof(Brush))]
    public class SeasonColourConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Season sn = (Season)value;
            ShowItem si = sn?.TheSeries?.Show;

            if (si is null) return Brushes.BlueViolet;

            if (si.IgnoreSeasons.Contains(sn.SeasonNumber)) return Brushes.Gray;

            if (TVSettings.Instance.ShowStatusColors != null)
            {
                return new SolidColorBrush(TVSettings.Instance.ShowStatusColors.GetEntry(true, false,
                        sn.Status(si.GetTimeZone()).ToString())??Colors.Black);
            }

            return Brushes.Black;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }


    [ValueConversion(typeof(Season), typeof(string))]
    class SeasonNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            Season s = (Season)value;
            if (s is null) return "Unknown Season";
            return ShowHtmlHelper.SeasonName(s);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
