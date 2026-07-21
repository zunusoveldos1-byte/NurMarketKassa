using NurMarketKassa.Core.Contracts;

namespace NurMarketKassa.AvaloniaHost.Services;

public sealed class AvaloniaUserPrompts : IUserPrompts
{
    public Task<bool> ConfirmAsync(string message) =>
        Task.FromResult(
            PosMessageBox.Show(message, "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes);

    public void ShowToast(string message, bool isWarning = false) =>
        PosMessageBox.Show(message, isWarning ? "Внимание" : "Сообщение",
            MessageBoxButton.OK, isWarning ? MessageBoxImage.Warning : MessageBoxImage.Information);

    public void ShowWarning(string message) =>
        PosMessageBox.Show(message, "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowError(string message) =>
        PosMessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
}
