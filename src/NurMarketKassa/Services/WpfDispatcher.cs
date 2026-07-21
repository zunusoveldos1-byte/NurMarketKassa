using System.Windows.Threading;
using NurMarketKassa.Ui.Shared;

namespace NurMarketKassa.Services;

public sealed class WpfDispatcher : IDispatcher
{
    public void Post(Action action) =>
        System.Windows.Application.Current.Dispatcher.BeginInvoke(action);

    public Task InvokeAsync(Action action) =>
        System.Windows.Application.Current.Dispatcher.InvokeAsync(action).Task;

    public Task InvokeAsync(Func<Task> action) =>
        System.Windows.Application.Current.Dispatcher.InvokeAsync(action).Task.Unwrap();
}
