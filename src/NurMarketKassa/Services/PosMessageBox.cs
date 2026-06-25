using System.Windows;
using NurMarketKassa.Views.Dialogs;

namespace NurMarketKassa.Services;

/// <summary>Модальные диалоги на XAML (без System.Windows.MessageBox).</summary>
public static class PosMessageBox
{
    public static MessageBoxResult Show(string messageBoxText) =>
        Show(messageBoxText, "Nur Market — Касса");

    public static MessageBoxResult Show(string messageBoxText, string caption) =>
        Show(messageBoxText, caption, MessageBoxButton.OK);

    public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button) =>
        Show(messageBoxText, caption, button, MessageBoxImage.None);

    public static MessageBoxResult Show(Window owner, string messageBoxText, string caption) =>
        Show(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

    public static MessageBoxResult Show(
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon) =>
        Show(null, messageBoxText, caption, button, icon);

    public static MessageBoxResult Show(
        Window? owner,
        string messageBoxText,
        string caption,
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None,
        MessageBoxResult defaultResult = MessageBoxResult.OK)
    {
        if (button == MessageBoxButton.YesNo
            && string.Equals(caption, "Подтверждение выхода", StringComparison.Ordinal))
        {
            return ExitConfirmationDialog.Show(owner)
                ? MessageBoxResult.Yes
                : MessageBoxResult.No;
        }

        if (button == MessageBoxButton.YesNo
            && string.Equals(caption, "Подтверждение", StringComparison.Ordinal)
            && string.Equals(messageBoxText, "Подтвердить оплату?", StringComparison.Ordinal))
        {
            return PaymentConfirmationDialog.Show(owner)
                ? MessageBoxResult.Yes
                : MessageBoxResult.No;
        }

        if (button == MessageBoxButton.YesNoCancel
            && string.Equals(caption, "Смена не закрыта", StringComparison.Ordinal))
        {
            return ShiftNotClosedDialog.Show(owner) switch
            {
                ShiftNotClosedDialogResult.CloseShift => MessageBoxResult.Yes,
                _ => MessageBoxResult.Cancel,
            };
        }

        switch (button)
        {
            case MessageBoxButton.YesNo:
            case MessageBoxButton.YesNoCancel:
            {
                var confirmed = PosConfirmDialog.Show(
                    owner,
                    caption,
                    messageBoxText,
                    confirmText: "Да",
                    cancelText: button == MessageBoxButton.YesNoCancel ? "Отмена" : "Нет");
                return confirmed ? MessageBoxResult.Yes : MessageBoxResult.No;
            }
            default:
            {
                var kind = MapAlertKind(icon);
                var buttonText = icon == MessageBoxImage.Question ? "ОК" : "Понятно";
                if (string.Equals(caption, "Принтер не подключен", StringComparison.OrdinalIgnoreCase)
                    || messageBoxText.Contains("принтер", StringComparison.OrdinalIgnoreCase)
                       && messageBoxText.Length < 80)
                {
                    PrinterNotConnectedDialog.ShowOk(owner, messageBoxText);
                    return MessageBoxResult.OK;
                }

                PosAlertDialog.Show(owner, caption, messageBoxText, kind, buttonText);
                return MessageBoxResult.OK;
            }
        }
    }

    private static PosAlertKind MapAlertKind(MessageBoxImage icon) =>
        icon switch
        {
            MessageBoxImage.Warning or MessageBoxImage.Exclamation => PosAlertKind.Warning,
            MessageBoxImage.Error or MessageBoxImage.Hand or MessageBoxImage.Stop => PosAlertKind.Error,
            MessageBoxImage.Information or MessageBoxImage.Asterisk => PosAlertKind.Info,
            _ => PosAlertKind.Info,
        };
}
