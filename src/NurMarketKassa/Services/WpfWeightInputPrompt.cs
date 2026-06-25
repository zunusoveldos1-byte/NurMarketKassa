using System.Globalization;
using System.Windows;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Services.Hardware;
using NurMarketKassa.Views;
using NurMarketKassa.Views.Dialogs;

namespace NurMarketKassa.Services;

public sealed class WpfWeightInputPrompt : IWeightInputPrompt
{
    private readonly ScaleWeightProvider _scaleProvider;

    public WpfWeightInputPrompt(ScaleWeightProvider scaleProvider) => _scaleProvider = scaleProvider;

    public Task<double?> PromptWeightKgAsync(string productTitle, CancellationToken cancellationToken = default)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
            return Task.FromResult(ShowDialog(productTitle));

        return dispatcher.InvokeAsync(() => ShowDialog(productTitle)).Task;
    }

    private double? ShowDialog(string productTitle)
    {
        if (Application.Current?.MainWindow is not MainWindow mainWindow)
            return null;

        var dlg = new WeighedProductDialog(
            productTitle,
            "",
            HardwareModeHelper.UsePhysicalScale() ? _scaleProvider.Scale : null)
        {
            Owner = mainWindow,
        };

        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.QuantityNormalized))
            return null;

        return double.TryParse(dlg.QuantityNormalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight)
            ? weight
            : null;
    }
}
