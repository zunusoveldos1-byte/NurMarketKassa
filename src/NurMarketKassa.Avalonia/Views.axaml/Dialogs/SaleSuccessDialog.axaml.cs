using Avalonia.Controls;
using Avalonia.Interactivity;
using NurMarketKassa.Services.Hardware;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public enum SaleSuccessDialogAction
{
    Close,
    Print,
    Preview,
}

public partial class SaleSuccessDialog : Window
{
    public SaleSuccessDialogAction Action { get; private set; } = SaleSuccessDialogAction.Close;

    public SaleSuccessDialog() : this(0) { }

    public SaleSuccessDialog(double totalAmount)
    {
        InitializeComponent();
        AmountText.Text = $"{totalAmount:0.00} сом";
    }

    private void PrintButton_Click(object? sender, RoutedEventArgs e)
    {
        if (!HardwareModeHelper.IsPrinterPortConfigured())
        {
            PrinterNotConnectedDialog.ShowOk(this, "Печать невозможна. Чековый аппарат не подключен.");
            Action = SaleSuccessDialogAction.Close;
            Close(true);
            return;
        }

        Action = SaleSuccessDialogAction.Print;
        Close(true);
    }

    private void PreviewButton_Click(object? sender, RoutedEventArgs e)
    {
        Action = SaleSuccessDialogAction.Preview;
        Close(true);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Action = SaleSuccessDialogAction.Close;
        Close(true);
    }
}
