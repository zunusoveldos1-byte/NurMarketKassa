using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NurMarketKassa.Admin.ViewModels;
using NurMarketKassa.Services;
using System.Windows;
using System.Windows.Threading;

namespace NurMarketKassa.Admin.Services;

public sealed class MetricsPollingBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private readonly IServiceProvider _services;

    public MetricsPollingBackgroundService(IServiceProvider services) => _services = services;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var monitor = _services.GetRequiredService<MySqlMonitorService>();
                var viewModel = _services.GetRequiredService<DashboardViewModel>();

                if (!monitor.IsEnabled)
                {
                    await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var terminals = await monitor.GetActiveTerminalsAsync(ct: stoppingToken).ConfigureAwait(false);
                var syncLogs = await monitor.GetSyncLogsAsync(ct: stoppingToken).ConfigureAwait(false);
                var sales = await monitor.GetSalesSummaryAsync(ct: stoppingToken).ConfigureAwait(false);
                var stock = await monitor.GetStockEventsAsync(ct: stoppingToken).ConfigureAwait(false);

                DateTime reportFrom;
                DateTime reportTo;
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    reportFrom = dispatcher.Invoke(() => viewModel.ReportFrom);
                    reportTo = dispatcher.Invoke(() => viewModel.ReportTo);
                }
                else
                {
                    reportFrom = DateTime.Today.AddDays(-6);
                    reportTo = DateTime.Today;
                }

                var ledger = await monitor.GetStockLedgerAsync(reportFrom, reportTo.AddDays(1), stoppingToken)
                    .ConfigureAwait(false);

                if (dispatcher != null)
                {
                    dispatcher.Invoke(() =>
                        viewModel.ApplySnapshot(terminals, syncLogs, sales, stock, ledger),
                        DispatcherPriority.DataBind);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Фоновый опрос не должен останавливать админку.
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
