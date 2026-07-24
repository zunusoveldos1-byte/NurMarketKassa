using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Services;

namespace NurMarketKassa.AvaloniaHost.Views.Customer;

/// <summary>
/// Окно экрана покупателя: список товаров, итог и статус оплаты.
/// </summary>
public sealed class CustomerDisplayWindow : Window
{
    private readonly CustomerDisplayStateService _state;
    private readonly TextBlock _statusText;
    private readonly TextBlock _totalText;
    private readonly ItemsControl _linesList;

    public CustomerDisplayWindow(CustomerDisplayStateService state)
    {
        _state = state;
        Title = "Экран покупателя";
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        WindowState = WindowState.FullScreen;
        Background = Brushes.White;

        _statusText = new TextBlock
        {
            FontSize = 28,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.DarkGreen,
            Margin = new Thickness(0, 0, 0, 16),
        };

        _totalText = new TextBlock
        {
            FontSize = 42,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.Black,
            Margin = new Thickness(0, 16, 0, 16),
        };

        _linesList = new ItemsControl();

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(32),
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Ваш чек",
                        FontSize = 36,
                        FontWeight = FontWeight.Bold,
                    },
                    _statusText,
                    _linesList,
                    _totalText,
                },
            },
        };

        _state.StateChanged += RefreshUi;
        Closed += (_, _) => _state.StateChanged -= RefreshUi;
        RefreshUi();
    }

    private void RefreshUi()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshUi);
            return;
        }

        var snapshot = _state.CurrentSnapshot;
        _totalText.Text = $"Итого: {snapshot.Total.ToString("0.00", CultureInfo.InvariantCulture)} сом";

        var lines = new ObservableCollection<string>();
        foreach (var line in snapshot.Lines)
        {
            lines.Add($"{line.Title}  ×{line.Quantity.ToString("0.###", CultureInfo.InvariantCulture)} {line.Unit}  " +
                      $"{line.LineTotal.ToString("0.00", CultureInfo.InvariantCulture)} сом");
        }

        _linesList.ItemsSource = lines;

        _statusText.Text = _state.CurrentStatus switch
        {
            CustomerDisplayPaymentStatus.Processing => _state.StatusMessage ?? "Идёт оплата...",
            CustomerDisplayPaymentStatus.Success => _state.StatusMessage ?? "Оплата прошла успешно",
            CustomerDisplayPaymentStatus.Failed => _state.StatusMessage ?? "Ошибка оплаты",
            _ => snapshot.Lines.Count == 0 ? "Добро пожаловать" : "",
        };

        _statusText.Foreground = _state.CurrentStatus switch
        {
            CustomerDisplayPaymentStatus.Failed => Brushes.DarkRed,
            CustomerDisplayPaymentStatus.Success => Brushes.DarkGreen,
            CustomerDisplayPaymentStatus.Processing => Brushes.DarkOrange,
            _ => Brushes.Gray,
        };
    }
}
