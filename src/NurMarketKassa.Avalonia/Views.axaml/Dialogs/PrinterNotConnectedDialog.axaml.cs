using Avalonia.Controls;
using NurMarketKassa.Ui.Shared;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class PrinterNotConnectedDialog : Window
{
    public PrinterNotConnectedDialog()
    {
        InitializeComponent();
    }

    public static PrinterNotConnectedResult Prompt(Window? owner)
        => PrinterNotConnectedResult.Cancel;
}
