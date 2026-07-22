using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public static class PosDialogHost
{
    public static Window ResolveOwner(Window? owner)
    {
        if (owner != null)
            return owner;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } main)
            return main;

        throw new InvalidOperationException("Нет активного окна для диалога.");
    }

    public static bool? Show(Window dialog, Window? owner)
    {
        owner = ResolveOwner(owner);
        return dialog.ShowDialog<bool?>(owner).GetAwaiter().GetResult();
    }

    public static Task<bool?> ShowAsync(Window dialog, Window? owner)
    {
        owner = ResolveOwner(owner);
        return dialog.ShowDialog<bool?>(owner);
    }

    public static Task<T?> ShowDialogAsync<T>(Window owner, Window dialog) where T : class =>
        dialog.ShowDialog<T?>(owner);
}
