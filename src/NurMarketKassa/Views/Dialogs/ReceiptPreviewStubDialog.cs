using NurMarketKassa.Services;

namespace NurMarketKassa.Views.Dialogs;

public sealed class ReceiptPreviewStubDialog : CustomDialogWindow
{
    public ReceiptPreviewStubDialog()
        : base(540)
    {
        PosLogger.Log("Preview requested. Feature not implemented yet.", "RECEIPT_PREVIEW");

        AddTitle("Предпросмотр чека");
        AddMessage("Функция находится в разработке.\n\nБудет доступна в ближайших версиях.");

        var ok = CreatePrimaryButton("Понятно");
        ok.Click += (_, _) => CloseWithResult(true);
        AddContent(ok);
    }
}
