using System.Windows;
using System.Windows.Threading;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Views;

namespace NurMarketKassa.Services;

public sealed class WpfUserPrompts : IUserPrompts
{
    private static Dispatcher AppDispatcher =>
        Application.Current?.Dispatcher
        ?? throw new InvalidOperationException("Диспетчер приложения недоступен.");

    public Task<bool> ConfirmAsync(string message) =>
        AppDispatcher.InvokeAsync(() => ShowConfirm(message), DispatcherPriority.Normal).Task;

    private static bool ShowConfirm(string message)
    {
        var result = PosMessageBox.Show(
            message,
            "Nur Market — Касса",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    public void ShowToast(string message, bool isWarning = false)
    {
        if (AppDispatcher.CheckAccess())
        {
            ShowToastCore(message, isWarning);
            return;
        }

        AppDispatcher.Invoke(() => ShowToastCore(message, isWarning));
    }

    public void ShowWarning(string message) => ShowToast(message, isWarning: true);

    public void ShowError(string message)
    {
        if (AppDispatcher.CheckAccess())
        {
            ShowErrorCore(message);
            return;
        }

        AppDispatcher.Invoke(() => ShowErrorCore(message));
    }

    private static void ShowToastCore(string message, bool isWarning)
    {
        if (Application.Current?.MainWindow is MainWindow mainWindow)
            mainWindow.ShowToast(message, isWarning);
    }

    private static void ShowErrorCore(string message) =>
        PosMessageBox.Show(
            message,
            "Nur Market — Касса",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
}
