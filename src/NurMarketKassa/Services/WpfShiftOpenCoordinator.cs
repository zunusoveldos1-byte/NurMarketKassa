using System.Windows;
using System.Windows.Threading;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Views;
using NurMarketKassa.Views.Dialogs;

namespace NurMarketKassa.Services;

public sealed class WpfShiftOpenCoordinator : IShiftOpenCoordinator
{
    public Task<bool> TryOpenShiftAsync(CancellationToken cancellationToken = default)
    {
        var dispatcher = Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("Диспетчер приложения недоступен.");

        return dispatcher.InvokeAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Application.Current?.MainWindow is not MainWindow mainWindow)
                return false;

            var dlg = new OpenShiftDialog { SuggestedBalance = mainWindow.GetCurrentBalance() };
            if (PosDialogHost.Show(dlg, mainWindow) != true)
                return false;

            await mainWindow.OpenShiftWithCashAsync(dlg.OpeningCash).ConfigureAwait(true);
            return !string.IsNullOrEmpty(App.ActiveShiftId);
        }, DispatcherPriority.Normal).Task.Unwrap();
    }
}
