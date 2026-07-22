using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class NoStockDialog : Window
{
    public bool GoToSite { get; private set; }

    public NoStockDialog() : this("", 0) { }

    public NoStockDialog(string productName, double availableStock, bool reservedInOtherReceipt = false)
    {
        InitializeComponent();
        TitleText.Text = "Товар закончился на складе";
        ProductNameText.Text = $"Товар:\n{productName}";
        MessageText.Text = reservedInOtherReceipt
            ? "Последняя единица уже продана\nили зарезервирована в другом чеке."
            : "На складе нет доступного количества для этого товара.";
        AvailableText.Text = $"Доступный остаток: {FormatQty(availableStock)}";
    }

    private static string FormatQty(double value) =>
        value.ToString(value % 1 < 1e-6 ? "0" : "0.###", CultureInfo.InvariantCulture);

    private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(true);
}
