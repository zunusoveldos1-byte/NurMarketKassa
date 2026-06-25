using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediatR;
using NurMarketKassa.Core.Application.Notifications;
using NurMarketKassa.Views;

namespace NurMarketKassa.Services;

public sealed class CartUiNotificationHandler :
    INotificationHandler<CartUpdatedNotification>,
    INotificationHandler<ScanStatusNotification>,
    INotificationHandler<ScanBusyNotification>
{
    public Task Handle(CartUpdatedNotification notification, CancellationToken cancellationToken)
    {
        return RunOnUiAsync(() =>
        {
            if (Application.Current?.MainWindow is MainWindow mainWindow)
                mainWindow.RebindCartUi();
        });
    }

    public Task Handle(ScanStatusNotification notification, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(notification.Message) && !notification.ClearBarcode)
            return Task.CompletedTask;

        return RunOnUiAsync(() =>
        {
            if (Application.Current?.MainWindow is not MainWindow mainWindow)
                return;

            if (!string.IsNullOrEmpty(notification.Message) && mainWindow.CartMessageText != null)
            {
                mainWindow.CartMessageText.Text = notification.Message;
                mainWindow.CartMessageText.Foreground = notification.Level switch
                {
                    ScanStatusLevel.Success => ThemeBrush("BrushUiStatusOk", new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99))),
                    ScanStatusLevel.Warning or ScanStatusLevel.Error => ThemeBrush("BrushUiStatusWarn", new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24))),
                    _ => ThemeBrush("BrushUiStatusMuted", new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF))),
                };
            }

            if (notification.ClearBarcode && mainWindow.BarcodeBox != null)
            {
                mainWindow.BarcodeBox.Text = "";
                mainWindow.BarcodeBox.Focus();
            }

            if (notification.ShowToast)
                mainWindow.ShowToast(notification.Message, notification.ToastWarning);
        });
    }

    public Task Handle(ScanBusyNotification notification, CancellationToken cancellationToken) =>
        RunOnUiAsync(() =>
        {
            if (Application.Current?.MainWindow is MainWindow mainWindow)
                mainWindow.SetScanBusy(notification.IsBusy);
        });

    private static Task RunOnUiAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    private static Brush ThemeBrush(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;
}
