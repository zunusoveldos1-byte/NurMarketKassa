using Avalonia.Interactivity;
using NurMarketKassa.AvaloniaHost.Views.Dialogs;

namespace NurMarketKassa.AvaloniaHost.Views;

public partial class FilterWindow : AppOverlayDialogBase
{
    public FilterWindow()
    {
        InitializeComponent();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => CloseWithAnimation(false);
    private void Reset_Click(object? sender, RoutedEventArgs e) { }
    private void Apply_Click(object? sender, RoutedEventArgs e) => CloseWithAnimation(true);
}
