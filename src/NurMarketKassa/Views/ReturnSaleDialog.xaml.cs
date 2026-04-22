using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NurMarketKassa.Models.Pos;
using NurMarketKassa.Services;

namespace NurMarketKassa.Views;

public partial class ReturnSaleDialog : Window
{
    private const int SalesPageSize = 35;

    private string? _currentSaleId;
    private int _salesPage;
    private readonly HashSet<string> _salesSeenIds = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<ReturnSaleListItemVm> Sales { get; } = new();
    public ObservableCollection<ReturnSaleLineVm> Lines { get; } = new();

    public ReturnSaleDialog()
    {
        InitializeComponent();
        DataContext = this;
        Lines.CollectionChanged += (_, _) => UpdateReceiptChrome();
        Loaded += OnFirstLoaded;
        UpdateReceiptChrome();
    }

    private async void OnFirstLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnFirstLoaded;
        await LoadSalesAsync(reset: true).ConfigureAwait(true);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void RefreshSales_Click(object sender, RoutedEventArgs e) =>
        await LoadSalesAsync(reset: true).ConfigureAwait(true);

    private async void MoreSales_Click(object sender, RoutedEventArgs e) =>
        await LoadSalesAsync(reset: false).ConfigureAwait(true);

    private async Task LoadSalesAsync(bool reset)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        ErrorText.Text = "";

        if (reset)
        {
            _salesPage = 1;
            Sales.Clear();
            _salesSeenIds.Clear();
        }
        else
            _salesPage++;

        var pageToRequest = reset ? 1 : _salesPage;
        SalesLoadButton.IsEnabled = false;
        MoreSalesButton.IsEnabled = false;
        try
        {
            var list = await App.Api
                .PosSalesListAsync(pageToRequest, SalesPageSize, App.PosCashboxId)
                .ConfigureAwait(true);

            var added = 0;
            foreach (var el in list)
            {
                var id = PosSaleRowFormatter.TrySaleId(el);
                if (string.IsNullOrEmpty(id) || !_salesSeenIds.Add(id))
                    continue;
                Sales.Add(
                    new ReturnSaleListItemVm
                    {
                        SaleId = id,
                        Summary = PosSaleRowFormatter.SummaryLine(el),
                    });
                added++;
            }

            if (reset && Sales.Count == 0)
            {
                ShowErr(
                    "Список продаж пуст или API не вернул данные. Попробуйте «Обновить» или введите ID продажи вручную.");
            }
            else if (!reset && added == 0)
            {
                MessageBox.Show(
                    this,
                    "Больше записей нет.",
                    "Продажи",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                _salesPage = Math.Max(1, _salesPage - 1);
            }
        }
        catch (ApiException ex)
        {
            if (reset)
                ShowErr(ex.Message);
            else
                MessageBox.Show(this, ex.Message, "Продажи", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (!reset)
                _salesPage = Math.Max(1, _salesPage - 1);
        }
        catch (HttpRequestException ex)
        {
            var m = string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message;
            if (reset)
                ShowErr(m);
            else
                MessageBox.Show(this, m, "Продажи", MessageBoxButton.OK, MessageBoxImage.Warning);
            if (!reset)
                _salesPage = Math.Max(1, _salesPage - 1);
        }
        catch (TaskCanceledException)
        {
            if (reset)
                ShowErr("Превышено время ожидания.");
            if (!reset)
                _salesPage = Math.Max(1, _salesPage - 1);
        }
        finally
        {
            SalesLoadButton.IsEnabled = true;
            MoreSalesButton.IsEnabled = true;
        }
    }

    private async void SelectSale_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
            return;
        var sid = btn.Tag as string ?? "";
        if (string.IsNullOrWhiteSpace(sid))
            return;
        await OpenSaleByIdAsync(sid.Trim()).ConfigureAwait(true);
    }

    private async void LoadById_Click(object sender, RoutedEventArgs e)
    {
        var id = (SaleIdBox.Text ?? "").Trim();
        if (id.Length == 0)
        {
            ShowErr("Введите ID продажи.");
            return;
        }

        await OpenSaleByIdAsync(id).ConfigureAwait(true);
    }

    private async Task OpenSaleByIdAsync(string saleId)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        ErrorText.Text = "";
        try
        {
            var sale = await App.Api.PosSaleGetAsync(saleId).ConfigureAwait(true);
            _currentSaleId = saleId;
            FillLinesFromSale(sale);
            UpdateReceiptChrome();
            if (Lines.Count == 0)
                ShowErr("В ответе сервера нет позиций с идентификатором строки для возврата.");
        }
        catch (ApiException ex)
        {
            ShowErr(ex.Message);
            _currentSaleId = null;
            Lines.Clear();
            UpdateReceiptChrome();
        }
        catch (HttpRequestException ex)
        {
            ShowErr(string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message);
            _currentSaleId = null;
            Lines.Clear();
            UpdateReceiptChrome();
        }
        catch (TaskCanceledException)
        {
            ShowErr("Превышено время ожидания.");
            _currentSaleId = null;
            Lines.Clear();
            UpdateReceiptChrome();
        }
    }

    private void FillLinesFromSale(JsonElement sale)
    {
        Lines.Clear();
        foreach (var it in CartDisplayHelper.EnumerateSaleLineItems(sale))
        {
            var lineId = CartDisplayHelper.TryItemId(it) ?? CartDisplayHelper.TrySaleLineRecordId(it);
            if (string.IsNullOrEmpty(lineId))
                continue;

            var title = CartDisplayHelper.ItemName(it);
            var sub = CartDisplayHelper.QuantityPriceLine(it);
            var sum = CartDisplayHelper.LineTotal(it);
            var can = !CartDisplayHelper.LineLooksFullyReturned(it);
            Lines.Add(
                new ReturnSaleLineVm
                {
                    LineId = lineId,
                    Title = title,
                    SubLine = sub,
                    LineSumText = $"Сумма: {sum} сом",
                    CanReturn = can,
                });
        }
    }

    private void SelectAllReturnable_Click(object sender, RoutedEventArgs e)
    {
        foreach (var line in Lines.Where(x => x.CanReturn))
            line.IsSelected = true;
    }

    private void ClearReturnSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var line in Lines)
            line.IsSelected = false;
    }

    private async void ReturnSelected_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentSaleId))
            return;

        var selected = Lines.Where(x => x.CanReturn && x.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(
                this,
                "Отметьте галочками хотя бы одну позицию для возврата.",
                "Возврат",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var reasonDlg = new ReturnLineReasonDialog(selected.Count, ReturnReasonDialogKind.LineItems) { Owner = this };
        if (reasonDlg.ShowDialog() != true)
            return;

        var reason = reasonDlg.ReasonText;
        var errors = new List<string>();
        foreach (var line in selected)
        {
            try
            {
                await App.Api
                    .PosSaleLineRefundAsync(_currentSaleId, line.LineId, reason)
                    .ConfigureAwait(true);
            }
            catch (ApiException ex)
            {
                errors.Add($"{line.Title}: {ex.Message}");
            }
            catch (HttpRequestException ex)
            {
                errors.Add(
                    $"{line.Title}: {(string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message)}");
            }
            catch (TaskCanceledException)
            {
                errors.Add($"{line.Title}: превышено время ожидания.");
            }
        }

        if (errors.Count == 0)
        {
            MessageBox.Show(
                this,
                selected.Count == 1
                    ? "Запрос на возврат отправлен. Проверьте результат в CRM."
                    : $"Запросы на возврат ({selected.Count} поз.) отправлены. Проверьте результат в CRM.",
                "Возврат",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else if (errors.Count < selected.Count)
        {
            MessageBox.Show(
                this,
                "Часть позиций не удалось вернуть:\n\n" + string.Join("\n", errors),
                "Возврат",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(
                this,
                "Не удалось оформить возврат:\n\n" + string.Join("\n", errors),
                "Возврат",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        try
        {
            var sale = await App.Api.PosSaleGetAsync(_currentSaleId).ConfigureAwait(true);
            FillLinesFromSale(sale);
            UpdateReceiptChrome();
        }
        catch
        {
            /* оставляем список как есть */
        }
    }

    private void UpdateReceiptChrome()
    {
        var hasSale = !string.IsNullOrEmpty(_currentSaleId);
        SelectedChequeBar.Visibility = hasSale ? Visibility.Visible : Visibility.Collapsed;
        ReturnWholeReceiptButton.IsEnabled = hasSale;

        if (!hasSale)
        {
            SelectedSaleText.Text = "";
            LinesPlaceholder.Text = "Сначала выберите чек в списке выше — здесь появится его содержимое.";
            LinesPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        if (Lines.Count == 0)
        {
            SelectedSaleText.Text = $"Выбран чек · {TruncateId(_currentSaleId)}";
            LinesPlaceholder.Text = "В этом чеке нет позиций с идентификатором строки для возврата через кассу.";
            LinesPlaceholder.Visibility = Visibility.Visible;
        }
        else
        {
            SelectedSaleText.Text =
                $"Выбран чек · {TruncateId(_currentSaleId)}  ·  позиций в чеке: {Lines.Count}";
            LinesPlaceholder.Visibility = Visibility.Collapsed;
        }
    }

    private static string TruncateId(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return "—";
        return id.Length > 36 ? id[..32] + "…" : id;
    }

    private async void ReturnWholeReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentSaleId))
        {
            MessageBox.Show(
                this,
                "Сначала выберите чек в списке выше.",
                "Возврат",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var reasonDlg = new ReturnLineReasonDialog(1, ReturnReasonDialogKind.FullReceipt) { Owner = this };
        if (reasonDlg.ShowDialog() != true)
            return;

        var answer = MessageBox.Show(
            this,
            "Оформить полный возврат всего чека одной операцией? Позиции по отдельности возвращать не потребуется.",
            "Полный возврат",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            await App.Api.PosSaleRefundOrVoidAsync(_currentSaleId, reasonDlg.ReasonText, default).ConfigureAwait(true);
            MessageBox.Show(
                this,
                "Запрос на полный возврат отправлен. Проверьте результат в CRM.",
                "Возврат",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            try
            {
                var sale = await App.Api.PosSaleGetAsync(_currentSaleId).ConfigureAwait(true);
                FillLinesFromSale(sale);
                UpdateReceiptChrome();
            }
            catch
            {
                Lines.Clear();
                UpdateReceiptChrome();
            }
        }
        catch (ApiException ex)
        {
            MessageBox.Show(this, ex.Message, "Возврат", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (HttpRequestException ex)
        {
            MessageBox.Show(
                this,
                string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message,
                "Возврат",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (TaskCanceledException)
        {
            MessageBox.Show(this, "Превышено время ожидания.", "Возврат", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowErr(string msg)
    {
        ErrorText.Text = msg;
        ErrorText.Visibility = Visibility.Visible;
    }
}
