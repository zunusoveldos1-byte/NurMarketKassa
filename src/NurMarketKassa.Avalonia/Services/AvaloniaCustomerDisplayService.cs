using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NurMarketKassa.AvaloniaHost.Views.Customer;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Services;

namespace NurMarketKassa.AvaloniaHost.Services;

/// <summary>
/// Avalonia-реализация экрана покупателя: открывает окно на втором мониторе и синхронизирует состояние.
/// Все обращения к Window выполняются только через Dispatcher.UIThread.
/// </summary>
public sealed class AvaloniaCustomerDisplayService : ICustomerDisplayService, IDisposable
{
    private readonly CustomerDisplayStateService _state;
    private CustomerDisplayWindow? _window;

    public AvaloniaCustomerDisplayService(CustomerDisplayStateService state)
    {
        _state = state;
        _state.StateChanged += OnStateChanged;
    }

    public void Show()
    {
        _state.Show();
        Dispatcher.UIThread.Post(EnsureWindowVisible);
    }

    public void Hide()
    {
        _state.Hide();
        Dispatcher.UIThread.Post(() => _window?.Hide());
    }

    public void UpdateCart(CustomerDisplayCartSnapshot snapshot)
    {
        // Состояние можно обновить с любого потока; RefreshUi окна сам маршалится.
        _state.UpdateCart(snapshot);
    }

    public void SetPaymentStatus(CustomerDisplayPaymentStatus status, string? message = null) =>
        _state.SetPaymentStatus(status, message);

    private void OnStateChanged()
    {
        if (!_state.IsVisible)
            return;

        Dispatcher.UIThread.Post(EnsureWindowVisible);
    }

    private void EnsureWindowVisible()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(EnsureWindowVisible);
            return;
        }

        try
        {
            if (_window is null)
            {
                _window = new CustomerDisplayWindow(_state);
                PlaceOnSecondScreen(_window);
            }

            if (!_window.IsVisible)
                _window.Show();
        }
        catch (Exception ex)
        {
            PosLogger.Log($"CustomerDisplay EnsureWindowVisible failed: {ex}", "CUSTOMER_DISPLAY");
        }
    }

    private static void PlaceOnSecondScreen(Window window)
    {
        var screens = window.Screens?.All;
        if (screens is { Count: >= 2 })
        {
            var target = screens[1];
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = target.WorkingArea.TopLeft;
            window.Width = target.WorkingArea.Width;
            window.Height = target.WorkingArea.Height;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.Width = 900;
            window.Height = 600;
        }
    }

    public void Dispose() => _state.StateChanged -= OnStateChanged;
}
