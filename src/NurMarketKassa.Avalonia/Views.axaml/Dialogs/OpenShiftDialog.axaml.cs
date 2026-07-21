using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class OpenShiftDialog : Window
{
    public decimal OpeningCash { get; set; }
    public decimal? SuggestedBalance { get; set; }
    public bool? DialogResult { get; set; }

    public OpenShiftDialog() => InitializeComponent();
}
