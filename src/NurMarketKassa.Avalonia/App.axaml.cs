using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NurMarketKassa.AvaloniaHost.Services;
using NurMarketKassa.AvaloniaHost.Views;
using NurMarketKassa.AvaloniaHost.Views.Dialogs;
using NurMarketKassa.AvaloniaHost.Views.MainKassir;
using NurMarketKassa.Configuration;
using NurMarketKassa.Services;
using NurMarketKassa.Services.Api;
using NurMarketKassa.Ui.Shared;
using NurMarketKassa.ViewModels;
using NurMarketKassa.ViewModels.Main;
using NurMarketKassa.ViewModels.Settings;

namespace NurMarketKassa.AvaloniaHost;

public partial class App : Application
{
    public static IHost? AppHost { get; private set; }

    // Bridge for ported services/views (same pattern as WPF App.*).
    public static ICatalogApiService CatalogApi { get; private set; } = null!;
    public static ISalesApiService SalesApi { get; private set; } = null!;
    public static IShiftApiService ShiftApi { get; private set; } = null!;
    public static IAuthApiService AuthApi { get; private set; } = null!;
    public static MySqlAuditService AuditDb { get; private set; } = null!;
    public static string? PosCashboxId { get; set; }
    public static bool ExitWithoutLoginRedirect { get; set; }
    public static bool IsOfflineBootstrap { get; set; }
    public static string? OfflineBootstrapMessage { get; set; }

    public static string? CurrentUserId
    {
        get => TryGetSession()?.CurrentUserId;
        set
        {
            var session = TryGetSession();
            if (session != null) session.CurrentUserId = value;
        }
    }

    public static T GetRequiredService<T>() where T : notnull =>
        AppHost!.Services.GetRequiredService<T>();

    public static void ApplyTheme(bool dark)
    {
        if (Current is not App app)
            return;

        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private static ResourceDictionary LoadThemeDictionary(string source) =>
        AvaloniaXamlLoader.Load(new Uri(source, UriKind.Absolute)) as ResourceDictionary
        ?? throw new InvalidOperationException($"Failed to load theme: {source}");

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        AppHost = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        AppHost.StartAsync().GetAwaiter().GetResult();
        UiDispatcherHolder.Current = AppHost.Services.GetRequiredService<IDispatcher>();
        AvaloniaHostServiceRegistration.InitializeAuthInfrastructure(AppHost.Services);
        AvaloniaHostServiceRegistration.InitializePosInfrastructure(AppHost.Services);

        var settings = AppHost.Services.GetRequiredService<AppSettings>();
        var session = AppHost.Services.GetRequiredService<IAppSession>();
        CatalogApi = AppHost.Services.GetRequiredService<ICatalogApiService>();
        SalesApi = AppHost.Services.GetRequiredService<ISalesApiService>();
        ShiftApi = AppHost.Services.GetRequiredService<IShiftApiService>();
        AuthApi = AppHost.Services.GetRequiredService<IAuthApiService>();
        AuditDb = AppHost.Services.GetRequiredService<MySqlAuditService>();

        NurMarketKassa.App.InitializeFromHost(settings, AuthApi, CatalogApi, SalesApi, ShiftApi, AuditDb, session);
        NurMarketKassa.App.SyncFromSession(session);
        PosCashboxId = NurMarketKassa.App.PosCashboxId;
        CurrentUserId = NurMarketKassa.App.CurrentUserId;

        ApplyTheme(UserPreferences.Instance.DarkTheme);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var loginWindow = AppHost.Services.GetRequiredService<LoginWindow>();
            desktop.MainWindow = loginWindow;
            loginWindow.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IDispatcher, AvaloniaDispatcher>();
        services.AddSingleton<ISettingsImagePicker, AvaloniaSettingsImagePicker>();
        services.AddSingleton<IOperatingSystemKeyboardService, WindowsOperatingSystemKeyboardService>();
        services.AddSingleton<IAppSession, AvaloniaAppSession>();
        services.AddSingleton<IWindowService, AvaloniaWindowService>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();

        AvaloniaHostServiceRegistration.AddAuthInfrastructure(services);
        AvaloniaHostServiceRegistration.AddPosInfrastructure(services);
        MainViewModelRegistration.AddMainWindowViewModels(services);

        services.AddTransient<SettingsViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<LoginWindow>();
        services.AddTransient<MainWindow>();
        services.AddTransient<CheckoutDialog>();
        services.AddTransient<AdminSupportWindow>();
        services.AddTransient<WarehouseWindow>();
        services.AddTransient<ShiftsHistoryWindow>();
        services.AddTransient<ShiftHistoryView>();
        services.AddTransient<ShiftSummaryView>();
        services.AddTransient<ServicesWindow>();
        services.AddTransient<SalesWindow>();
        services.AddTransient<FinanceWindow>();
        services.AddTransient<PosSettingsWindow>();
        services.AddTransient<FilterWindow>();
        services.AddTransient<OpenShiftDialog>();
        services.AddTransient<CloseShiftDialog>();
        services.AddTransient<CashOperationsDialog>();
        services.AddTransient<CashHistoryDialog>();
        services.AddTransient<ReturnSaleDialog>();
        services.AddTransient<ReturnLineReasonDialog>();
        services.AddTransient<ProductDetailDialog>();
        services.AddTransient<WeighedProductDialog>();
        services.AddTransient<OrderDiscountDialog>();
        services.AddTransient<DeferredCartsDialog>();
        services.AddTransient<ReceiptPreviewDialog>();
        services.AddTransient<FinanceDateRangeDialog>();
        services.AddTransient<FrmKeyboard>();
        services.AddTransient<NoStockDialog>();
        services.AddTransient<NewOperationDialog>();
        services.AddTransient<ShiftDetailsDialog>();
        services.AddTransient<ShiftActionsMenu>();
        services.AddTransient<PosAlertDialog>();
        services.AddTransient<PosConfirmDialog>();
        services.AddTransient<PaymentConfirmationDialog>();
        services.AddTransient<PrinterNotConnectedDialog>();
        services.AddTransient<SaleSuccessDialog>();
        services.AddTransient<PaymentStockBlockedDialog>();
        services.AddTransient<DeferredStockIssuesDialog>();
    }

    private static IAppSession? TryGetSession()
    {
        try { return AppHost?.Services.GetService<IAppSession>(); }
        catch { return null; }
    }
}
