using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NurMarketKassa.Admin.Services;
using NurMarketKassa.Admin.ViewModels;
using NurMarketKassa.Configuration;
using NurMarketKassa.Core.Application;
using NurMarketKassa.Core.Contracts;
using NurMarketKassa.Services;

namespace NurMarketKassa.Admin;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = null!;
    public static NurMarketApiClient Api { get; private set; } = null!;
    public static MySqlAuditService AuditDb { get; private set; } = null!;
    public static MySqlMonitorService Monitor { get; private set; } = null!;
    public static DashboardViewModel Dashboard { get; private set; } = null!;
    public static IReportExportService ReportExport { get; private set; } = null!;

    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        Settings = AppSettings.Load();

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddSingleton<AppSettings>(_ => Settings);
                services.AddSingleton<MySqlSettings>(resolver =>
                    resolver.GetRequiredService<AppSettings>().MySql);
                services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(55) });
                services.AddSingleton(sp => new NurMarketApiClient(
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<AppSettings>()));
                services.AddSingleton(sp => new MySqlAuditService(sp.GetRequiredService<MySqlSettings>()));
                services.AddSingleton(sp => new MySqlMonitorService(sp.GetRequiredService<MySqlSettings>()));
                services.AddSingleton<IMySqlConnectionSettings, WpfMySqlConnectionSettings>();
                services.AddSingleton<IStockService, StockService>();
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<IReportExportService, ReportExportService>();
                services.AddHostedService<MetricsPollingBackgroundService>();
            })
            .Build();

        await _host.StartAsync();

        Api = _host.Services.GetRequiredService<NurMarketApiClient>();
        AuditDb = _host.Services.GetRequiredService<MySqlAuditService>();
        Monitor = _host.Services.GetRequiredService<MySqlMonitorService>();
        Dashboard = _host.Services.GetRequiredService<DashboardViewModel>();
        ReportExport = _host.Services.GetRequiredService<IReportExportService>();

        AuditDb.Initialize();
        _host.Services.GetRequiredService<IStockService>().Initialize();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
