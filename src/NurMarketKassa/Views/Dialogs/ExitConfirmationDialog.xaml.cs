using System.Windows;

namespace NurMarketKassa.Views.Dialogs;

public partial class ExitConfirmationDialog : PosDialogWindowBase
{
    public ExitConfirmationDialog()
    {
        InitializeComponent();
    }

    public static bool Show(Window? owner) =>
        PosDialogHost.Show(new ExitConfirmationDialog(), owner) == true;

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
