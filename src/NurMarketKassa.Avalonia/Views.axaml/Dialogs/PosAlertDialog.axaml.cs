using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public enum PosAlertKind
{
    Info,
    Warning,
    Error,
    Success,
}

public partial class PosAlertDialog : Window
{
    public PosAlertDialog()
    {
        InitializeComponent();
    }

    public static void Show(Window? owner, string title, string message) { }

    public static void Show(Window? owner, string title, string message, PosAlertKind kind) { }

    public static void Show(Window? owner, string title, string message, PosAlertKind kind, string okText) { }

    public static Task ShowAsync(Window? owner, string title, string message) => Task.CompletedTask;
}
