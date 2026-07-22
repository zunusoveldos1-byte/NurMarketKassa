using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NurMarketKassa.AvaloniaHost.Views.Customer;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Services;

namespace NurMarketKassa.AvaloniaHost.Services;

/// <summary>
/// Avalonia-реализация экрана покупателя: открывает окно на втором мониторе и синхронизирует состояние.
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
        Dispatcher.UIThread.Post(EnsureWindowVisible);
        _state.Show();
    }

    public void Hide()
    {
        _state.Hide();
        Dispatcher.UIThread.Post(() => _window?.Hide());
    }

    public void UpdateCart(CustomerDisplayCartSnapshot snapshot) => _state.UpdateCart(snapshot);

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
        if (_window is null)
        {
            _window = new CustomerDisplayWindow(_state);
            PlaceOnSecondScreen(_window);
        }

        if (!_window.IsVisible)
            _window.Show();
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
