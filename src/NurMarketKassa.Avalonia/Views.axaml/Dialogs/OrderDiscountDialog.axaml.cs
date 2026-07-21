using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class OrderDiscountDialog : Window
{
    public OrderDiscountDialog()
    {
        InitializeComponent();
    }


    public decimal? DiscountValue { get; set; }
    public bool? DialogResult { get; set; }

}
