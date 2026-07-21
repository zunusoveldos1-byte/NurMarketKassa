using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class PosConfirmDialog : Window
{
    public PosConfirmDialog()
    {
        InitializeComponent();
    }


    public static bool Confirm(Window? owner, string title, string message) => false;
    public static Task<bool> ConfirmAsync(Window? owner, string title, string message) => Task.FromResult(false);

}
