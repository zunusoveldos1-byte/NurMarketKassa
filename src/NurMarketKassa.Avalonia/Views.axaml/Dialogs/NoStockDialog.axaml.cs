using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class NoStockDialog : Window
{
    public bool GoToSite { get; private set; }

    public NoStockDialog()
    {
        InitializeComponent();
    }

    public NoStockDialog(string productName, double availableStock, bool reservedInOtherReceipt = false)
        : this()
    {
        TitleText.Text = "Товар закончился на складе";
        ProductNameText.Text = $"Товар:\n{productName}";
        MessageText.Text = reservedInOtherReceipt
            ? "Последняя единица уже продана\nили зарезервирована в другом чеке."
            : "На складе нет доступного количества для этого товара.";
        AvailableText.Text = $"Доступный остаток: {FormatQty(availableStock)}";
    }

    private static string FormatQty(double value) =>
        value.ToString(value % 1 < 1e-6 ? "0" : "0.###", CultureInfo.InvariantCulture);

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        // Close must run synchronously on the UI thread while ShowDialog's nested loop
        // is active. Dispatcher.Post after a blocking GetResult() never runs → dialog stuck.
        if (Dispatcher.UIThread.CheckAccess())
        {
            Close(true);
            return;
        }

        Dispatcher.UIThread.Post(() => Close(true));
    }
}
