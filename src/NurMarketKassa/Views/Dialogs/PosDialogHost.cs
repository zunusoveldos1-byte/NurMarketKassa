using System.Windows;

namespace NurMarketKassa.Views.Dialogs;

internal static class PosDialogHost
{
    public static Window ResolveOwner(Window? owner)
    {
        if (owner != null)
            return owner;

        var active = Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive && w != Application.Current?.MainWindow)
            ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);

        return active ?? Application.Current?.MainWindow
               ?? throw new InvalidOperationException("Нет активного окна для диалога.");
    }

    public static bool? Show(Window dialog, Window? owner)
    {
        dialog.Owner = ResolveOwner(owner);
        PosDialogLayout.AttachOverlayToOwner(dialog, dialog.Owner);
        return dialog.ShowDialog();
    }
}
    