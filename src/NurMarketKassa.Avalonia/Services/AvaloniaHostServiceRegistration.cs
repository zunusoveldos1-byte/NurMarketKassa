using System.Text;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NurMarketKassa.Configuration;
using NurMarketKassa.Core.Application;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Interfaces;
using NurMarketKassa.Services;
using NurMarketKassa.Services.Api;
using NurMarketKassa.Services.Hardware;
using NurMarketKassa.Ui.Shared;

namespace NurMarketKassa.AvaloniaHost.Services;

/// <summary>
/// Этот файл регистрирует в DI Avalonia-хоста инфраструктурные сервисы:
/// SQLite, REST API клиенты сайта, каталог, корзину, смену и вспомогательные зависимости кассы.
/// </summary>
internal static class AvaloniaHostServiceRegistration
{
    public static void AddAuthInfrastructure(IServiceCollection services)
    {
        var settings = AppSettings.Load();
        UserPreferences.LoadFromDiskAndMergeDefaults(settings);

        services.AddSingleton(settings);
        services.AddSingleton(_ => DatabaseService.Instance);
        services.AddSingleton<ILocalAccountsStore, LocalAccountsManager>();
        services.AddSingleton<IConnectivityService, ConnectivityService>();
        services.AddSingleton<IOfflineLoginSupport, OfflineLoginSupport>();
        services.AddSingleton<NurMarketApiClient>();
        services.AddSingleton<IAuthApiService, AuthApiService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<IAuthService, PosAuthService>();
    }

    public static void AddPosInfrastructure(IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(
            typeof(IPosSessionService).Assembly,
            typeof(PosCheckoutService).Assembly));

        services.AddSingleton<ICatalogApiService, CatalogApiService>();
        services.AddSingleton<ISalesApiService, SalesApiService>();
        services.AddSingleton<IShiftApiService, ShiftApiService>();
        services.AddSingleton<ICatalogCacheService, AvaloniaCatalogCacheService>();
        services.AddSingleton<ICartService, CartService>();
        services.AddSingleton<IShiftStateService, ShiftStateService>();
        services.AddSingleton<Core.Contracts.IOfflinePosStateStore, OfflinePosStateStoreAdapter>();
        services.AddSingleton<IReceiptPrinterService, VirtualReceiptPrinterService>();
        services.AddSingleton<ICashShiftService, CashShiftService>();
        services.AddSingleton<IPosCheckoutService, PosCheckoutService>();
        services.AddSingleton<IDeferredCartService, DeferredCartService>();
        services.AddSingleton<IMySqlConnectionSettings, AvaloniaMySqlConnectionSettings>();
        services.AddSingleton<IStockAuditWriter, AvaloniaStockAuditWriter>();
        services.AddSingleton<IStockService, StockService>();
        services.AddSingleton<CustomerDisplayStateService>();
        services.AddSingleton<ICustomerDisplayService, AvaloniaCustomerDisplayService>();

        services.AddSingleton<MySqlSettings>(sp => sp.GetRequiredService<AppSettings>().MySql);
        services.AddSingleton<MySqlAuditService>(sp =>
        {
            var audit = new MySqlAuditService(sp.GetRequiredService<MySqlSettings>());
            try { audit.Initialize(); } catch { /* optional */ }
            return audit;
        });

        services.AddSingleton<IUserPrompts, AvaloniaUserPrompts>();
        services.AddSingleton<IBarcodeInputService, AvaloniaKeyboardWedgeBarcodeService>();
        services.AddSingleton<IPosCheckoutUiFlow, AvaloniaPosCheckoutUiFlow>();
        services.AddSingleton<IWeightInputPrompt, AvaloniaWeightInputPrompt>();
        services.AddSingleton<IShiftOpenCoordinator, AvaloniaShiftOpenCoordinator>();
        services.AddSingleton<ScaleWeightProvider>();
    }

    public static void InitializeAuthInfrastructure(IServiceProvider services)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            services.GetRequiredService<DatabaseService>().EnsureSchema();
            services.GetRequiredService<ILocalAccountsStore>().EnsureSchema();
        }
        catch { /* offline */ }
    }

    public static void InitializePosInfrastructure(IServiceProvider services)
    {
        try
        {
            services.GetRequiredService<IStockService>().Initialize();
        }
        catch (Exception ex)
        {
            PosLogger.Log($"STOCK init failed: {ex.Message}", "STOCK");
        }

        try
        {
            LocalProductRepository.Instance.EnsureSchema();
            _ = LocalProductRepository.Instance.WarmUpCacheAsync();
        }
        catch (Exception ex)
        {
            PosLogger.Log($"CATALOG warm-up failed: {ex.Message}", "CATALOG");
        }
    }
}
