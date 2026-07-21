using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class CashOperationsDialog : Window
{
    public CashOperationsDialog()
    {
        InitializeComponent();
    }


    public bool? DialogResult { get; set; }

}
