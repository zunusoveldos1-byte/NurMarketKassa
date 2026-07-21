using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NurMarketKassa.Configuration;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Interfaces;
using NurMarketKassa.Services;
using NurMarketKassa.Services.Api;

namespace NurMarketKassa.AvaloniaHost.Services;

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
        services.AddSingleton<ICatalogApiService, CatalogApiService>();
        services.AddSingleton<ISalesApiService, SalesApiService>();
        services.AddSingleton<IShiftApiService, ShiftApiService>();

        services.AddSingleton<MySqlSettings>(sp => sp.GetRequiredService<AppSettings>().MySql);
        services.AddSingleton<MySqlAuditService>(sp =>
        {
            var audit = new MySqlAuditService(sp.GetRequiredService<MySqlSettings>());
            try { audit.Initialize(); } catch { /* optional */ }
            return audit;
        });

        services.AddSingleton<IUserPrompts, AvaloniaUserPrompts>();
        services.AddSingleton<IBarcodeInputService, AvaloniaKeyboardWedgeBarcodeService>();
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
        _ = services;
    }
}
