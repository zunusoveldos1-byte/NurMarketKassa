namespace NurMarketKassa.Ui.Shared;

/// <summary>POS-specific checkout UI hooks (stock checks, post-payment dialogs).</summary>
public interface IPosCheckoutUiFlow
{
    /// <summary>Returns false to abort checkout before opening payment dialog.</summary>
    Task<bool> PrepareCheckoutAsync();

    Task ShowPaymentSuccessAsync(double totalAmount, bool defaultPrintReceipt);
}
