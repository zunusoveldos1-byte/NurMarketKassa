using System;
using System.Globalization;
using System.Windows.Data;

namespace NurMarketKassa.Converters
{
    public class LessThanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double v && parameter is string s &&
                double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double threshold))
            {
                return v < threshold;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}