using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class ReceiptPreviewDialog : Window
{
    public ReceiptPreviewDialog() => InitializeComponent();
    public ReceiptPreviewDialog(string content) : this() { }
    public ReceiptPreviewDialog(object? a, object? b) : this() { }
}
