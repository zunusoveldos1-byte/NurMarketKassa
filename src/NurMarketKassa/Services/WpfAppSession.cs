using NurMarketKassa.Ui.Shared;

namespace NurMarketKassa.Services;

/// <summary>WPF adapter: delegates session fields to static <see cref="App"/> bridge.</summary>
public sealed class WpfAppSession : IAppSession
{
    public string? CurrentUserId
    {
        get => App.CurrentUserId;
        set => App.CurrentUserId = value;
    }

    public string? ActiveShiftId
    {
        get => App.ActiveShiftId;
        set => App.ActiveShiftId = value;
    }

    public string? ActiveTerminal
    {
        get => App.PosCashboxId;
        set => App.PosCashboxId = value;
    }

    public string? PosCashboxDisplayName
    {
        get => App.PosCashboxDisplayName;
        set => App.PosCashboxDisplayName = value;
    }

    public bool IsShiftOpen => !string.IsNullOrEmpty(App.ActiveShiftId);

    public bool IsOfflineBootstrap
    {
        get => App.IsOfflineBootstrap;
        set => App.IsOfflineBootstrap = value;
    }

    public string? OfflineBootstrapMessage
    {
        get => App.OfflineBootstrapMessage;
        set => App.OfflineBootstrapMessage = value;
    }
}
