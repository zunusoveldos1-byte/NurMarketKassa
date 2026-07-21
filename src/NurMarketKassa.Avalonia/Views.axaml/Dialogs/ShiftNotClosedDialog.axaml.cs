using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public enum ShiftNotClosedDialogResult
{
    Cancel,
    CloseShift,
}

public partial class ShiftNotClosedDialog : Window
{
    public ShiftNotClosedDialogResult Result { get; private set; } = ShiftNotClosedDialogResult.Cancel;

    public ShiftNotClosedDialog()
    {
        InitializeComponent();
    }

    public static ShiftNotClosedDialogResult Prompt(Window? owner) => ShiftNotClosedDialogResult.Cancel;
}
