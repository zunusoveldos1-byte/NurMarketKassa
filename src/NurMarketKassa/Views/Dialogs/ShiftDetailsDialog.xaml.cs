using System.Windows;
using System.Windows.Media;
using NurMarketKassa.Models;

namespace NurMarketKassa.Views.Dialogs;

public partial class ShiftDetailsDialog
{
    public ShiftDetailsDialog(ShiftModel shift)
    {
        InitializeComponent();
        ShiftNumberText.Text = shift.ShiftNumber;
        OpenedAtText.Text = shift.OpenedAt?.ToString("dd.MM.yyyy HH:mm") ?? "—";
        ClosedAtText.Text = shift.ClosedAt?.ToString("dd.MM.yyyy HH:mm") ?? "—";
        CashierText.Text = shift.Cashier;
        StatusText.Text = shift.Status;
        RevenueText.Text = $"{shift.Revenue:N2} сом";

        if (shift.IsActive)
        {
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(209, 250, 229));
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(5, 150, 105));
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(5, 150, 105));
        }
        else
        {
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(254, 226, 226));
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(185, 28, 28));
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseWithAnimation(false);
}
