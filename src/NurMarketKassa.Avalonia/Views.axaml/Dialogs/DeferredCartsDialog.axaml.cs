using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class DeferredCartsDialog : Window
{
    public DeferredCartsDialog()
    {
        InitializeComponent();
    }


    public bool? DialogResult { get; set; }

}
