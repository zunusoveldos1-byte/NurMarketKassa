using Avalonia.Controls;
using NurMarketKassa.AvaloniaHost.Views.Dialogs;

namespace NurMarketKassa.AvaloniaHost.Services;

public enum MessageBoxButton
{
    OK = 0,
    OKCancel = 1,
    YesNoCancel = 3,
    YesNo = 4,
}

public enum MessageBoxImage
{
    None = 0,
    Error = 16,
    Hand = 16,
    Stop = 16,
    Question = 32,
    Exclamation = 48,
    Warning = 48,
    Asterisk = 64,
    Information = 64,
}

public enum MessageBoxResult
{
    None = 0,
    OK = 1,
    Cancel = 2,
    Yes = 6,
    No = 7,
}

/// <summary>Модальные диалоги Avalonia (упрощённый аналог WPF PosMessageBox на период миграции).</summary>
public static class PosMessageBox
{
    public static MessageBoxResult Show(string messageBoxText) =>
        Show(null, messageBoxText, "Nur Market — Касса", MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string messageBoxText, string caption) =>
        Show(null, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button) =>
        Show(null, messageBoxText, caption, button, MessageBoxImage.None);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage image) =>
        Show(null, messageBoxText, caption, button, image);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption) =>
        Show(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(Window? owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage image)
    {
        var kind = image switch
        {
            MessageBoxImage.Error or MessageBoxImage.Hand or MessageBoxImage.Stop => PosAlertKind.Error,
            MessageBoxImage.Warning or MessageBoxImage.Exclamation => PosAlertKind.Warning,
            MessageBoxImage.Asterisk or MessageBoxImage.Information => PosAlertKind.Info,
            _ => PosAlertKind.Info,
        };

        if (button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel or MessageBoxButton.OKCancel)
        {
            PosAlertDialog.Show(owner, caption, messageBoxText, kind, "OK");
            return MessageBoxResult.Yes;
        }

        PosAlertDialog.Show(owner, caption, messageBoxText, kind);
        return MessageBoxResult.OK;
    }

    public static ShiftNotClosedDialogResult ShowShiftNotClosed(Window? owner) =>
        ShiftNotClosedDialog.Prompt(owner);

    public static MessageBoxResult ShowPrinterNotConnected(Window? owner) =>
        MessageBoxResult.OK;
}

public static class PosDialogs
{
    public static void ShowReceiptPreviewStub(Window? owner) =>
        PosAlertDialog.Show(owner, "Предпросмотр", "Предпросмотр чека будет доступен в Avalonia UI.", PosAlertKind.Info);
}
