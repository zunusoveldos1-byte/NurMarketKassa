namespace NurMarketKassa.AvaloniaHost.Services;

/// <summary>Deferred host callbacks so main-window ViewModels can invoke UI actions without circular DI.</summary>
public sealed class MainWindowHostBridge
{
    public Views.MainWindow? Window { get; set; }
}