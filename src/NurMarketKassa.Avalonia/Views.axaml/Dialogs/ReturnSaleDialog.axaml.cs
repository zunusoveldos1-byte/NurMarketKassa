using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class ReturnSaleDialog : Window
{
    public ReturnSaleDialog()
    {
        InitializeComponent();
    }


    public bool? DialogResult { get; set; }

}
