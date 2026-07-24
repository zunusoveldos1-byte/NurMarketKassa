using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using NurMarketKassa.Models;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class ShiftDetailsDialog : Window
{
    public bool? DialogResult { get; set; }

    public ShiftDetailsDialog() => InitializeComponent();

    public ShiftDetailsDialog(object? model) : this()
    {
        if (model is ShiftModel shift)
            BindShift(shift);
    }

    public ShiftDetailsDialog(ShiftModel shift) : this() => BindShift(shift);

    private void BindShift(ShiftModel shift)
    {
        ShiftNumberText.Text = string.IsNullOrWhiteSpace(shift.ShiftNumber) ? "—" : shift.ShiftNumber;
        OpenedAtText.Text = shift.OpenedAt?.ToString("dd.MM.yyyy HH:mm") ?? "—";
        ClosedAtText.Text = shift.ClosedAt?.ToString("dd.MM.yyyy HH:mm") ?? "—";
        CashierText.Text = string.IsNullOrWhiteSpace(shift.Cashier) ? "—" : shift.Cashier;
        StatusText.Text = string.IsNullOrWhiteSpace(shift.Status) ? "—" : shift.Status;
        RevenueText.Text = $"{shift.Revenue:N2} сом";
        SalesCountText.Text = "—";
        CashText.Text = "—";
        CardText.Text = "—";

        if (shift.IsActive)
        {
            StatusBadge.Background = new SolidColorBrush(Color.Parse("#D1FAE5"));
            StatusDot.Fill = new SolidColorBrush(Color.Parse("#059669"));
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#059669"));
        }
        else
        {
            StatusBadge.Background = new SolidColorBrush(Color.Parse("#FEE2E2"));
            StatusDot.Fill = new SolidColorBrush(Color.Parse("#B91C1C"));
            StatusText.Foreground = new SolidColorBrush(Color.Parse("#B91C1C"));
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close(false);
    }
}
