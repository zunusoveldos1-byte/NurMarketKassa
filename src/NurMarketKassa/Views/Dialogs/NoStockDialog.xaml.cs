using System.Globalization;
using System.Windows;

namespace NurMarketKassa.Views.Dialogs;

public partial class NoStockDialog : Window
{
    public bool GoToSite { get; private set; }

    public NoStockDialog(string productName, double availableStock, bool reservedInOtherReceipt = false)
    {
        InitializeComponent();
        TitleText.Text = "Товар закончился на складе";
        ProductNameText.Text = $"Товар:\n{productName}";
        MessageText.Text = reservedInOtherReceipt
            ? "Последняя единица уже продана\nили зарезервирована в другом чеке."
            : "На складе нет доступного количества для этого товара.";
        AvailableText.Text = $"Доступный остаток: {FormatQty(availableStock)}";
        BtnGoToSite.Visibility = Visibility.Collapsed;
        BtnCancel.Content = "ОК";
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

    private void BtnGoToSite_Click(object sender, RoutedEventArgs e)
    {
        GoToSite = true;
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
