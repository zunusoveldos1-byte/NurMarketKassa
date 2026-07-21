using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public static class PosDialogHost
{
    public static bool? Show(Window owner, Window dialog)
    {
        dialog.ShowDialog(owner);
        return true;
    }

    public static async Task<T?> ShowDialogAsync<T>(Window owner, Window dialog) where T : class
    {
        return await dialog.ShowDialog<T>(owner);
    }

    public static Task ShowAsync(Window owner, Window dialog) => dialog.ShowDialog(owner);
}
