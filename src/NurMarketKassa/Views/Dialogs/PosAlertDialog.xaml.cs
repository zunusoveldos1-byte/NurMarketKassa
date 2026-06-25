using System.Windows;

namespace NurMarketKassa.Views.Dialogs;

public enum PosAlertKind
{
    Info,
    Warning,
    Error,
    Success,
}

public partial class PosAlertDialog : PosDialogWindowBase
{
    public PosAlertDialog(string title, string message, PosAlertKind kind = PosAlertKind.Info, string buttonText = "Понятно")
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        OkButton.Content = buttonText;
        OkButton.Style = kind == PosAlertKind.Error
            ? (Style)FindResource("PosDialogDangerButton")
            : (Style)FindResource("PosDialogPrimaryButton");
    }

    public static void Show(Window? owner, string title, string message, PosAlertKind kind = PosAlertKind.Info, string buttonText = "Понятно")
    {
        var dlg = new PosAlertDialog(title, message, kind, buttonText);
        PosDialogHost.Show(dlg, owner);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
