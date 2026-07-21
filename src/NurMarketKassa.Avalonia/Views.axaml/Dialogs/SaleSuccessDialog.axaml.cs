using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class SaleSuccessDialog : Window
{
    public SaleSuccessDialog()
    {
        InitializeComponent();
    }


    public static void Show(Window? owner, string message) { }

}
