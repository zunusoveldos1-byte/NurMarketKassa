using System.Globalization;
using System.Windows;
using System.Windows.Data;
using NurMarketKassa.Admin.ViewModels;

namespace NurMarketKassa.Admin.Converters;

public sealed class AdminSectionToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not AdminSection current || parameter is not string target)
            return Visibility.Collapsed;

        return current.ToString() == target ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
