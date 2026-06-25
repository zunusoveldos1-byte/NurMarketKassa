using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using NurMarketKassa.Services;

namespace NurMarketKassa.Admin.ViewModels;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private readonly object _collectionSync = new();
    private bool _isLoading;
    private string _statusText = "";
    private string _dbStatusText = "Проверка подключения…";
    private DateTime _reportFrom = DateTime.Today.AddDays(-6);
    private DateTime _reportTo = DateTime.Today;
    private AdminSection _selectedSection = AdminSection.Terminals;

    public DashboardViewModel()
    {
        BindingOperations.EnableCollectionSynchronization(Terminals, _collectionSync);
        BindingOperations.EnableCollectionSynchronization(SyncLogs, _collectionSync);
        BindingOperations.EnableCollectionSynchronization(SalesSummary, _collectionSync);
        BindingOperations.EnableCollectionSynchronization(StockEvents, _collectionSync);
        BindingOperations.EnableCollectionSynchronization(StockLedger, _collectionSync);
    }

    public ObservableCollection<MySqlMonitorService.ActiveTerminalRow> Terminals { get; } = new();
    public ObservableCollection<MySqlMonitorService.SyncLogRow> SyncLogs { get; } = new();
    public ObservableCollection<MySqlMonitorService.SalesSummaryRow> SalesSummary { get; } = new();
    public ObservableCollection<MySqlMonitorService.StockSummaryRow> StockEvents { get; } = new();
    public ObservableCollection<MySqlMonitorService.StockLedgerRow> StockLedger { get; } = new();

    public bool IsLoading
    {
        get => _isLoading;
        set => SetOnUi(() => _isLoading = value, nameof(IsLoading));
    }

    public string StatusText
    {
        get => _statusText;
        set => SetOnUi(() => _statusText = value, nameof(StatusText));
    }

    public string DbStatusText
    {
        get => _dbStatusText;
        set => SetOnUi(() => _dbStatusText = value, nameof(DbStatusText));
    }

    public DateTime ReportFrom
    {
        get => _reportFrom;
        set => SetOnUi(() => _reportFrom = value.Date, nameof(ReportFrom));
    }

    public DateTime ReportTo
    {
        get => _reportTo;
        set => SetOnUi(() => _reportTo = value.Date, nameof(ReportTo));
    }

    public AdminSection SelectedSection
    {
        get => _selectedSection;
        set => SetOnUi(() =>
        {
            _selectedSection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTerminalsSelected));
            OnPropertyChanged(nameof(IsLogsSelected));
            OnPropertyChanged(nameof(IsFinanceSelected));
        }, nameof(SelectedSection));
    }

    public bool IsTerminalsSelected => SelectedSection == AdminSection.Terminals;
    public bool IsLogsSelected => SelectedSection == AdminSection.Logs;
    public bool IsFinanceSelected => SelectedSection == AdminSection.Finance;

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task RefreshAsync(MySqlMonitorService monitor, bool showLoadingOverlay = true)
    {
        if (showLoadingOverlay)
            IsLoading = true;

        try
        {
            if (!monitor.IsEnabled)
            {
                RunOnUiThread(() =>
                {
                    StatusText = "MySQL отключён. Включите MySql.Enabled и укажите ConnectionString в appsettings.json.";
                    DbStatusText = "MySQL: отключён";
                    ClearAll();
                });
                return;
            }

            await monitor.VerifyConnectionAsync().ConfigureAwait(false);
            RunOnUiThread(() => DbStatusText = "MySQL: подключено");

            var terminals = await monitor.GetActiveTerminalsAsync().ConfigureAwait(false);
            var syncLogs = await monitor.GetSyncLogsAsync().ConfigureAwait(false);
            var sales = await monitor.GetSalesSummaryAsync().ConfigureAwait(false);
            var stock = await monitor.GetStockEventsAsync().ConfigureAwait(false);
            var ledger = await monitor.GetStockLedgerAsync(ReportFrom, ReportTo.AddDays(1)).ConfigureAwait(false);

            RunOnUiThread(() => ApplySnapshot(terminals, syncLogs, sales, stock, ledger));
        }
        catch (Exception ex)
        {
            RunOnUiThread(() =>
            {
                StatusText = $"Ошибка: {ex.Message}";
                DbStatusText = "MySQL: ошибка";
                ClearAll();
            });
            throw;
        }
        finally
        {
            if (showLoadingOverlay)
                IsLoading = false;
        }
    }

    public void ApplySnapshot(
        IReadOnlyList<MySqlMonitorService.ActiveTerminalRow> terminals,
        IReadOnlyList<MySqlMonitorService.SyncLogRow> syncLogs,
        IReadOnlyList<MySqlMonitorService.SalesSummaryRow> sales,
        IReadOnlyList<MySqlMonitorService.StockSummaryRow> stock,
        IReadOnlyList<MySqlMonitorService.StockLedgerRow>? ledger = null)
    {
        RunOnUiThread(() =>
        {
            ReplaceCollection(Terminals, terminals);
            ReplaceCollection(SyncLogs, syncLogs);
            ReplaceCollection(SalesSummary, sales);
            ReplaceCollection(StockEvents, stock);
            if (ledger != null)
                ReplaceCollection(StockLedger, ledger);

            var totalRows = Terminals.Count + SyncLogs.Count + SalesSummary.Count + StockEvents.Count;
            StatusText = totalRows == 0
                ? $"Обновлено: {DateTime.Now:HH:mm:ss} · данных нет (запустите кассу для записи в audit_events)"
                : $"Обновлено: {DateTime.Now:HH:mm:ss} · терминалов: {Terminals.Count}, записей: {totalRows}";
        });
    }

    public void ClearAll()
    {
        RunOnUiThread(() =>
        {
            Terminals.Clear();
            SyncLogs.Clear();
            SalesSummary.Clear();
            StockEvents.Clear();
            StockLedger.Clear();
        });
    }

    private static void ReplaceCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }

    private void SetOnUi(Action apply, string propertyName)
    {
        RunOnUiThread(() =>
        {
            apply();
            OnPropertyChanged(propertyName);
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action, DispatcherPriority.DataBind);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
