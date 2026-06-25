using System.Globalization;
using System.Text;
using System.Windows;
using NurMarketKassa.Services;

namespace NurMarketKassa.Views.Dialogs;

public partial class PaymentStockBlockedDialog : Window
{
    public PaymentStockBlockedDialog(IReadOnlyList<(string Title, StockLineStatus Status)> issues)
    {
        InitializeComponent();
        var sb = new StringBuilder();
        sb.AppendLine("Недостаточно товара на складе для оплаты:");
        sb.AppendLine();

        foreach (var (title, status) in issues)
        {
            sb.AppendLine(title);
            sb.AppendLine($"В чеке: {FormatQty(status.LineQty)}");
            sb.AppendLine($"Доступно: {FormatQty(status.Available)}");
            sb.AppendLine();
        }

        BodyText.Text = sb.ToString().TrimEnd();
    }

    private static string FormatQty(double value) =>
        value.ToString(value % 1 < 1e-6 ? "0" : "0.###", CultureInfo.InvariantCulture);

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (Owner == null)
            return;

        Left = Owner.Left;
        Top = Owner.Top;
        Width = Owner.ActualWidth;
        Height = Owner.ActualHeight;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
