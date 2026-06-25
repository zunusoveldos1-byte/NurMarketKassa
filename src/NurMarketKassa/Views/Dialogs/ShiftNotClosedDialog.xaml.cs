using System.Windows;

namespace NurMarketKassa.Views.Dialogs;

public enum ShiftNotClosedDialogResult
{
    Cancel,
    CloseShift,
}

public partial class ShiftNotClosedDialog : PosDialogWindowBase
{
    public ShiftNotClosedDialogResult Result { get; private set; } = ShiftNotClosedDialogResult.Cancel;

    public ShiftNotClosedDialog()
    {
        InitializeComponent();
    }

    public static ShiftNotClosedDialogResult Show(Window? owner)
    {
        var dlg = new ShiftNotClosedDialog();
        PosDialogHost.Show(dlg, owner);
        return dlg.Result;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Result = ShiftNotClosedDialogResult.Cancel;
        DialogResult = false;
        Close();
    }

    private void CloseShiftButton_Click(object sender, RoutedEventArgs e)
    {
        Result = ShiftNotClosedDialogResult.CloseShift;
        DialogResult = true;
        Close();
    }
}
