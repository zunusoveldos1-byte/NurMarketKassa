using System.Windows;

namespace NurMarketKassa.Views.Dialogs;

public enum PrinterNotConnectedResult
{
    Cancel,
    ContinueWithoutPrint,
    Ok,
}

public partial class PrinterNotConnectedDialog : PosDialogWindowBase
{
    public PrinterNotConnectedResult Result { get; private set; } = PrinterNotConnectedResult.Cancel;

    private PrinterNotConnectedDialog(bool checkoutMode)
    {
        InitializeComponent();

        if (checkoutMode)
        {
            Title = "Чековый аппарат не подключен";
            TitleText.Text = "Чековый аппарат не подключен";
            MessageText.Text =
                "Вы выбрали печать чека, однако чековый аппарат не подключён.\n" +
                "Подключите чековый аппарат либо отключите печать чека и продолжите без печати.";
            CancelButton.Content = "Отмена";
            ProceedButton.Content = "Отключить печать и продолжить";
            return;
        }

        Title = "Принтер не подключен";
        TitleText.Text = "Принтер не подключён";
        MessageText.Text = "Чековый аппарат не подключен.";
        TwoButtonRow.Visibility = Visibility.Collapsed;
        OkButton.Visibility = Visibility.Visible;
    }

    public static PrinterNotConnectedResult ShowCheckout(Window? owner)
    {
        var dlg = new PrinterNotConnectedDialog(checkoutMode: true);
        PosDialogHost.Show(dlg, owner);
        return dlg.Result;
    }

    public static void ShowOk(Window? owner, string? message = null)
    {
        var dlg = new PrinterNotConnectedDialog(checkoutMode: false);
        if (!string.IsNullOrWhiteSpace(message))
            dlg.MessageText.Text = message;
        PosDialogHost.Show(dlg, owner);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Result = PrinterNotConnectedResult.Cancel;
        DialogResult = false;
        Close();
    }

    private void ProceedButton_Click(object sender, RoutedEventArgs e)
    {
        Result = PrinterNotConnectedResult.ContinueWithoutPrint;
        DialogResult = true;
        Close();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Result = PrinterNotConnectedResult.Ok;
        DialogResult = true;
        Close();
    }
}
