using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class ExitConfirmationDialog : Window
{
    public bool? DialogResult { get; set; }

    public ExitConfirmationDialog() => InitializeComponent();

    public static bool ConfirmExit(Window? owner)
    {
        // Stub until full dialog UI is restored.
        return false;
    }
}
