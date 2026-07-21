using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class CloseShiftDialog : Window
{
    public decimal ClosingCash { get; set; }
    public decimal? SuggestedBalance { get; set; }
    public bool? DialogResult { get; set; }

    public CloseShiftDialog() => InitializeComponent();
}
