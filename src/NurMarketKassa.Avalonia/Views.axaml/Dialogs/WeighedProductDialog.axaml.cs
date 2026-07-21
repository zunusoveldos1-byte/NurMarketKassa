using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class WeighedProductDialog : Window
{
    public WeighedProductDialog()
    {
        InitializeComponent();
    }


    public double? WeightKg { get; set; }
    public bool? DialogResult { get; set; }

}
