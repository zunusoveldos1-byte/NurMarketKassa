using System.Globalization;
using Avalonia.Threading;
using NurMarketKassa.AvaloniaHost.Services;
using NurMarketKassa.AvaloniaHost.Views.Dialogs;
using NurMarketKassa.AvaloniaHost.Views.MainKassir;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Services;
using NurMarketKassa.Services.Hardware;
using NurMarketKassa.Ui.Shared;

namespace NurMarketKassa.AvaloniaHost.Services;

public sealed class AvaloniaPosCheckoutUiFlow : IPosCheckoutUiFlow
{
    private readonly ICartService _cart;
    private readonly MainWindowHostBridge _bridge;

    public AvaloniaPosCheckoutUiFlow(ICartService cart, MainWindowHostBridge bridge)
    {
        _cart = cart;
        _bridge = bridge;
    }

    public Task<bool> PrepareCheckoutAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            var owner = _bridge.Window;
            if (owner == null)
                return true;

            var issues = StockAvailabilityService.EvaluateCurrentCart(_cart);
            if (issues.Count == 0)
                return true;

            var dialog = new PaymentStockBlockedDialog(issues);
            PosDialogHost.Show(dialog, owner);
            return false;
        }).GetTask();
    }

    public Task ShowPaymentSuccessAsync(double totalAmount, bool defaultPrintReceipt)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            var owner = _bridge.Window;
            if (owner == null)
                return;

            PosDialogs.ShowPaymentSuccess(owner, totalAmount, defaultPrintReceipt);
        }).GetTask();
    }
}

public sealed class AvaloniaWeightInputPrompt : IWeightInputPrompt
{
    private readonly ScaleWeightProvider _scaleProvider;
    private readonly MainWindowHostBridge _bridge;

    public AvaloniaWeightInputPrompt(ScaleWeightProvider scaleProvider, MainWindowHostBridge bridge)
    {
        _scaleProvider = scaleProvider;
        _bridge = bridge;
    }

    public Task<double?> PromptWeightKgAsync(string productTitle, CancellationToken cancellationToken = default)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owner = _bridge.Window;
            if (owner == null)
                return (double?)null;

            var scale = HardwareModeHelper.UsePhysicalScale() ? _scaleProvider.Scale : null;
            var dlg = new WeighedProductDialog(productTitle, "", scale);
            if (PosDialogHost.Show(dlg, owner) != true || string.IsNullOrWhiteSpace(dlg.QuantityNormalized))
                return null;

            return double.TryParse(dlg.QuantityNormalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight)
                ? weight
                : null;
        }, DispatcherPriority.Normal, cancellationToken).GetTask();
    }
}

public sealed class AvaloniaShiftOpenCoordinator : IShiftOpenCoordinator
{
    private readonly MainWindowHostBridge _bridge;
    private readonly ICashShiftService _cashShiftService;

    public AvaloniaShiftOpenCoordinator(MainWindowHostBridge bridge, ICashShiftService cashShiftService)
    {
        _bridge = bridge;
        _cashShiftService = cashShiftService;
    }

    public async Task<bool> TryOpenShiftAsync(CancellationToken cancellationToken = default)
    {
        var window = _bridge.Window;
        if (window == null)
            return false;

        return await window.OpenShiftFromCoordinatorAsync(cancellationToken).ConfigureAwait(true);
    }
}
