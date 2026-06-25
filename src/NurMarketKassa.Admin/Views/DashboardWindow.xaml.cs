using System.Windows;
using Microsoft.Win32;
using NurMarketKassa.Admin.ViewModels;

namespace NurMarketKassa.Admin.Views;

public partial class DashboardWindow : Window
{
    private DashboardViewModel ViewModel => (DashboardViewModel)DataContext;

    public DashboardWindow()
    {
        InitializeComponent();
        DataContext = App.Dashboard;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RefreshDataAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDataAsync();

    private void NavTerminals_Click(object sender, RoutedEventArgs e) =>
        ViewModel.SelectedSection = AdminSection.Terminals;

    private void NavLogs_Click(object sender, RoutedEventArgs e) =>
        ViewModel.SelectedSection = AdminSection.Logs;

    private void NavFinance_Click(object sender, RoutedEventArgs e) =>
        ViewModel.SelectedSection = AdminSection.Finance;

    private async Task RefreshDataAsync()
    {
        try
        {
            await ViewModel.RefreshAsync(App.Monitor, showLoadingOverlay: true).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Не удалось загрузить данные из MySQL:\n\n{ex.Message}",
                "Ошибка подключения к БД",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ExportSalesExcel_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync(isSales: true, usePdf: false);

    private async void ExportSalesPdf_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync(isSales: true, usePdf: true);

    private async void ExportLedgerExcel_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync(isSales: false, usePdf: false);

    private async void ExportLedgerPdf_Click(object sender, RoutedEventArgs e) =>
        await ExportAsync(isSales: false, usePdf: false);

    private async Task ExportAsync(bool isSales, bool usePdf)
    {
        if (!App.Monitor.IsEnabled)
        {
            MessageBox.Show(this, "MySQL отключён в appsettings.json.", "Экспорт",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var from = ViewModel.ReportFrom;
        var to = ViewModel.ReportTo;
        if (to < from)
        {
            MessageBox.Show(this, "Дата «по» не может быть раньше даты «с».", "Экспорт",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = usePdf ? "PDF (*.pdf)|*.pdf" : "Excel (*.xlsx)|*.xlsx",
            FileName = isSales
                ? $"sales_{from:yyyyMMdd}_{to:yyyyMMdd}.{(usePdf ? "pdf" : "xlsx")}"
                : $"stock_ledger_{from:yyyyMMdd}_{to:yyyyMMdd}.{(usePdf ? "pdf" : "xlsx")}",
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            if (isSales)
                await App.ReportExport.ExportSalesSummaryAsync(from, to, dialog.FileName, usePdf).ConfigureAwait(true);
            else
                await App.ReportExport.ExportStockLedgerAsync(from, to, dialog.FileName, usePdf).ConfigureAwait(true);

            MessageBox.Show(this, $"Отчёт сохранён:\n{dialog.FileName}", "Экспорт",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось сохранить отчёт:\n\n{ex.Message}", "Экспорт",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
