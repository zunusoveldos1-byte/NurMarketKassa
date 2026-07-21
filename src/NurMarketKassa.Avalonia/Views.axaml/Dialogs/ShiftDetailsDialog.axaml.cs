using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class ShiftDetailsDialog : Window
{
    public bool? DialogResult { get; set; }

    public ShiftDetailsDialog() => InitializeComponent();
    public ShiftDetailsDialog(object? model) : this() { }
}
