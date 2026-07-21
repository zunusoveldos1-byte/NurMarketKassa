using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class PaymentConfirmationDialog : Window
{
    public PaymentConfirmationDialog()
    {
        InitializeComponent();
    }


    public static bool Confirm(Window? owner) => false;

}
