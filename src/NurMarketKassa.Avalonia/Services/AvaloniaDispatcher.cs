using Avalonia.Threading;
using UiDispatcher = NurMarketKassa.Ui.Shared.IDispatcher;

namespace NurMarketKassa.AvaloniaHost.Services;

public sealed class AvaloniaDispatcher : UiDispatcher
{
    public void Post(Action action) =>
        Dispatcher.UIThread.Post(action);

    public async Task InvokeAsync(Action action) =>
        await Dispatcher.UIThread.InvokeAsync(action);

    public async Task InvokeAsync(Func<Task> action) =>
        await Dispatcher.UIThread.InvokeAsync(action);
}
