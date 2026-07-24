using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

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

    /// <summary>
    /// Shows a modal dialog. Safe to call from the UI thread (pumps the dispatcher
    /// instead of deadlocking on <c>GetResult()</c>).
    /// </summary>
    public static bool? Show(Window dialog, Window? owner)
    {
        owner = ResolveOwner(owner);
        var task = dialog.ShowDialog<bool?>(owner);

        if (task.IsCompleted)
            return task.GetAwaiter().GetResult();

        if (!Dispatcher.UIThread.CheckAccess())
            return task.GetAwaiter().GetResult();

        // UI thread: pump until the dialog closes so Click → Close can complete.
        using var cts = new CancellationTokenSource();
        _ = task.ContinueWith(
            static (t, state) => ((CancellationTokenSource)state!).Cancel(),
            cts,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        Dispatcher.UIThread.MainLoop(cts.Token);
        return task.GetAwaiter().GetResult();
    }

    public static Task<bool?> ShowAsync(Window dialog, Window? owner)
    {
        owner = ResolveOwner(owner);
        return dialog.ShowDialog<bool?>(owner);
    }

    public static Task<T?> ShowDialogAsync<T>(Window owner, Window dialog) where T : class =>
        dialog.ShowDialog<T?>(owner);
}
