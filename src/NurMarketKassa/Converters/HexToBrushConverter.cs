using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace NurMarketKassa.Converters;

/// <summary>Converts a hex color string (e.g. #DC2626) to <see cref="Brush"/>.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                if (ColorConverter.ConvertFromString(hex.Trim()) is Color color)
                    return new SolidColorBrush(color);
            }
            catch
            {
                /* fall through */
            }
        }

        return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B")!);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
