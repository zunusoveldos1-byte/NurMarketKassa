using System.Globalization;
using System.Windows;
using NurMarketKassa.Services.Hardware;

namespace NurMarketKassa.Views.Dialogs;

public enum SaleSuccessDialogAction
{
    Close,
    Print,
    Preview,
}

public partial class SaleSuccessDialog : PosDialogWindowBase
{
    public SaleSuccessDialogAction Action { get; private set; } = SaleSuccessDialogAction.Close;

    public SaleSuccessDialog(double totalAmount)
    {
        InitializeComponent();
        AmountText.Text = totalAmount.ToString("0.00", CultureInfo.InvariantCulture) + " сом";
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (!HardwareModeHelper.IsPrinterPortConfigured())
        {
            PrinterNotConnectedDialog.ShowOk(this, "Печать невозможна. Чековый аппарат не подключен.");
            Action = SaleSuccessDialogAction.Close;
            DialogResult = true;
            Close();
            return;
        }

        Action = SaleSuccessDialogAction.Print;
        DialogResult = true;
        Close();
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        Action = SaleSuccessDialogAction.Preview;
        DialogResult = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Action = SaleSuccessDialogAction.Close;
        DialogResult = true;
        Close();
    }
}
