using System.Windows;
using NurMarketKassa.Views.Dialogs;

namespace NurMarketKassa.Services;

public static class PosDialogs
{
    public static bool ConfirmYesNo(Window? owner, string message, string title = "Подтверждение") =>
        PosConfirmDialog.Show(owner, title, message);

    public static void Info(Window? owner, string message, string title = "Сообщение") =>
        PosAlertDialog.Show(owner, title, message, PosAlertKind.Info);

    public static void Warning(Window? owner, string message, string title = "Внимание") =>
        PosAlertDialog.Show(owner, title, message, PosAlertKind.Warning);

    public static void Error(Window? owner, string message, string title = "Ошибка") =>
        PosAlertDialog.Show(owner, title, message, PosAlertKind.Error);

    public static PaymentSuccessDialogResult? ShowPaymentSuccess(Window? owner, double totalAmount, bool defaultPrintReceipt)
    {
        var dlg = new SaleSuccessDialog(totalAmount);
        if (PosDialogHost.Show(dlg, owner) != true)
            return null;

        return dlg.Action switch
        {
            SaleSuccessDialogAction.Print => new PaymentSuccessDialogResult { PrintReceipt = true },
            SaleSuccessDialogAction.Preview => HandlePreview(owner),
            _ => new PaymentSuccessDialogResult { PrintReceipt = false },
        };
    }

    private static PaymentSuccessDialogResult HandlePreview(Window? owner)
    {
        ShowReceiptPreviewStub(owner);
        return new PaymentSuccessDialogResult { PrintReceipt = false };
    }

    public static PrinterNotConnectedResult ShowPrinterNotConnected(Window? owner) =>
        PrinterNotConnectedDialog.ShowCheckout(owner);

    public static void ShowReceiptPreviewStub(Window? owner)
    {
        PosLogger.Log("Preview requested. Feature not implemented yet.", "RECEIPT_PREVIEW");
        PosAlertDialog.Show(
            owner,
            "Предпросмотр",
            "Предпросмотр чека будет доступен в следующей версии.",
            PosAlertKind.Info);
    }
}

public sealed class PaymentSuccessDialogResult
{
    public bool PrintReceipt { get; init; }
}
