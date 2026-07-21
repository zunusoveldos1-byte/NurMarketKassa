using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class CheckoutDialog : Window
{
    public CheckoutDialog()
    {
        InitializeComponent();
    }


    public bool? DialogResult { get; set; }
    public decimal PaidAmount { get; set; }

}
