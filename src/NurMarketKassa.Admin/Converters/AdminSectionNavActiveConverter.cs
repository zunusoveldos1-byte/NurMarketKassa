using System.Globalization;
using System.Windows.Data;
using NurMarketKassa.Admin.ViewModels;

namespace NurMarketKassa.Admin.Converters;

public sealed class AdminSectionNavActiveConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return false;

        if (values[0] is not AdminSection section)
            return false;

        var tag = values[1]?.ToString() ?? "";
        return section.ToString() == tag;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
