using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using NurMarketKassa.Services;

namespace NurMarketKassa.Admin.Converters;

public sealed class TerminalStatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush OnlineBrush = new(Color.FromArgb(0x33, 0x34, 0xD3, 0x99));
    private static readonly SolidColorBrush IdleBrush = new(Color.FromArgb(0x33, 0xFB, 0xBF, 0x24));
    private static readonly SolidColorBrush OfflineBrush = new(Color.FromArgb(0x28, 0x9C, 0xA3, 0xAF));
    private static readonly SolidColorBrush DefaultBrush = new(Colors.Transparent);

    static TerminalStatusToBrushConverter()
    {
        OnlineBrush.Freeze();
        IdleBrush.Freeze();
        OfflineBrush.Freeze();
        DefaultBrush.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value switch
        {
            MySqlMonitorService.TerminalStatus.Online => OnlineBrush,
            MySqlMonitorService.TerminalStatus.Idle => IdleBrush,
            MySqlMonitorService.TerminalStatus.Offline => OfflineBrush,
            _ => DefaultBrush,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
