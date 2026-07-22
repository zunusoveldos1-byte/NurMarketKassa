using Avalonia.Controls;
using NurMarketKassa.AvaloniaHost.Services;
using NurMarketKassa.AvaloniaHost.Views.Dialogs;
using NurMarketKassa.Ui.Shared;

namespace NurMarketKassa.AvaloniaHost.Services;

public sealed class AvaloniaDialogService : IDialogService
{
    public Task<bool> ConfirmAsync(string title, string message)
    {
        var owner = GetOwner();
        return Task.FromResult(PosConfirmDialog.Show(owner, title, message));
    }

    public Task ShowInfoAsync(string message)
    {
        PosAlertDialog.Show(GetOwner(), "Сообщение", message, PosAlertKind.Info);
        return Task.CompletedTask;
    }

    public Task ShowErrorAsync(string message)
    {
        PosAlertDialog.Show(GetOwner(), "Ошибка", message, PosAlertKind.Error);
        return Task.CompletedTask;
    }

    public Task<PrinterNotConnectedResult> ShowPrinterNotConnectedAsync()
    {
        var result = PrinterNotConnectedDialog.ShowCheckout(GetOwner());
        return Task.FromResult(result);
    }

    public Task<bool> ConfirmPaymentAsync() =>
        Task.FromResult(PaymentConfirmationDialog.Show(GetOwner()));

    private static Window GetOwner() => PosDialogHost.ResolveOwner(null);
}
