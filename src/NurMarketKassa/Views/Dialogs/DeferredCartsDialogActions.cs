using NurMarketKassa.Services;

namespace NurMarketKassa.Views.Dialogs;

/// <summary>Колбэки для операций с отложенными чеками из <see cref="DeferredCartsDialog"/>.</summary>
public sealed class DeferredCartsDialogActions
{
    public Func<IReadOnlyList<DeferredCartEntry>, Task<bool>>? MergeIntoCurrentAsync { get; init; }

    public Func<DeferredCartEntry, Task<bool>>? OpenAsSeparateAsync { get; init; }
}
