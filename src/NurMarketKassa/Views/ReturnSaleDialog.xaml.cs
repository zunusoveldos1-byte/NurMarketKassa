using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NurMarketKassa.Models.Pos;
using NurMarketKassa.Services;

#nullable disable

namespace NurMarketKassa.Views
{
    public partial class ReturnSaleDialog : Window, INotifyPropertyChanged
    {
        private const int SalesPageSize = 35;
        private string _currentSaleId;
        private int _salesPage;
        private readonly HashSet<string> _salesSeenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly CollectionViewSource _salesViewSource = new CollectionViewSource();
        private string _searchFilter = "";
        private bool _isBusy;
        private string _currentUserId;

        public ObservableCollection<ReturnSaleListItemVm> Sales { get; } = new ObservableCollection<ReturnSaleListItemVm>();
        public ObservableCollection<ReturnSaleLineVm> Lines { get; } = new ObservableCollection<ReturnSaleLineVm>();

        public event PropertyChangedEventHandler PropertyChanged;

        public ICollectionView SalesView => _salesViewSource.View;

        public bool IsBusy
        {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); }
        }

        public ReturnSaleDialog()
        {
            InitializeComponent();
            _currentUserId = App.CurrentUserId;
            DataContext = this;
            _salesViewSource.Source = Sales;
            _salesViewSource.Filter += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_searchFilter))
                {
                    e.Accepted = true;
                }
                else if (e.Item is ReturnSaleListItemVm vm)
                {
                    e.Accepted = vm.SaleId?.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ?? false;
                }
                else
                {
                    e.Accepted = false;
                }
            };

            Lines.CollectionChanged += (_, _) => UpdateReceiptChrome();
            Loaded += OnFirstLoaded;
            UpdateReceiptChrome();
        }

        private async void OnFirstLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnFirstLoaded;
            await LoadSalesAsync(true).ConfigureAwait(true);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private async void RefreshSales_Click(object sender, RoutedEventArgs e) =>
            await LoadSalesAsync(true).ConfigureAwait(true);

        private async void MoreSales_Click(object sender, RoutedEventArgs e) =>
            await LoadSalesAsync(false).ConfigureAwait(true);

        private async Task LoadSalesAsync(bool reset)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            ErrorText.Text = "";
            IsBusy = true;

            if (reset)
            {
                _salesPage = 1;
                Sales.Clear();
                _salesSeenIds.Clear();
            }
            else
            {
                _salesPage++;
            }

            int page = reset ? 1 : _salesPage;

            try
            {
                var jsonElementList = await App.Api.PosSalesListAsync(page, SalesPageSize, App.PosCashboxId).ConfigureAwait(true);

                bool hasCustomerId = false;
                foreach (var el in jsonElementList)
                {
                    if (el.TryGetProperty("customer_id", out _))
                    {
                        hasCustomerId = true;
                        break;
                    }
                }

                int added = 0;
                foreach (var row in jsonElementList)
                {
                    if (!string.IsNullOrWhiteSpace(_currentUserId) && hasCustomerId &&
                        row.TryGetProperty("customer_id", out var cid) &&
                        !string.Equals(cid.GetString(), _currentUserId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var saleId = PosSaleRowFormatter.TrySaleId(row);
                    if (!string.IsNullOrEmpty(saleId) && _salesSeenIds.Add(saleId))
                    {
                        Sales.Add(new ReturnSaleListItemVm
                        {
                            SaleId = saleId,
                            Summary = PosSaleRowFormatter.SummaryLine(row)
                        });
                        added++;
                    }
                }

                if (reset && Sales.Count == 0)
                {
                    ShowErr("Список продаж пуст или API не вернул данные. Попробуйте «Обновить» или введите ID продажи вручную.");
                }
                else if (!reset && added == 0)
                {
                    MessageBox.Show(this, "Больше записей нет.", "Продажи", MessageBoxButton.OK, MessageBoxImage.Asterisk);
                    _salesPage = Math.Max(1, _salesPage - 1);
                }
            }
            catch (ApiException ex)
            {
                if (reset) ShowErr(ex.Message);
                else MessageBox.Show(this, ex.Message, "Продажи", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                if (reset) return;
                _salesPage = Math.Max(1, _salesPage - 1);
            }
            catch (HttpRequestException ex)
            {
                string msg = string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message;
                if (reset) ShowErr(msg);
                else MessageBox.Show(this, msg, "Продажи", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                if (reset) return;
                _salesPage = Math.Max(1, _salesPage - 1);
            }
            catch (TaskCanceledException)
            {
                if (reset) ShowErr("Превышено время ожидания.");
                if (reset) return;
                _salesPage = Math.Max(1, _salesPage - 1);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async void SelectSale_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string saleId || string.IsNullOrWhiteSpace(saleId))
                return;
            await OpenSaleByIdAsync(saleId.Trim()).ConfigureAwait(true);
        }

        private async void LoadById_Click(object sender, RoutedEventArgs e)
        {
            string saleId = (SaleIdBox.Text ?? "").Trim();
            if (saleId.Length == 0)
                ShowErr("Введите ID продажи.");
            else
                await OpenSaleByIdAsync(saleId).ConfigureAwait(true);
        }

        private async Task OpenSaleByIdAsync(string saleId)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            ErrorText.Text = "";
            IsBusy = true;
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
            finally
            {
                IsBusy = false;
            }
        }

        private void FillLinesFromSale(JsonElement sale)
        {
            Lines.Clear();
            foreach (var lineItem in CartDisplayHelper.EnumerateSaleLineItems(sale))
            {
                string lineId = CartDisplayHelper.TryItemId(lineItem) ?? CartDisplayHelper.TrySaleLineRecordId(lineItem);
                if (string.IsNullOrEmpty(lineId)) continue;

                string title = CartDisplayHelper.ItemName(lineItem);
                string subLine = CartDisplayHelper.QuantityPriceLine(lineItem);
                string lineTotal = CartDisplayHelper.LineTotal(lineItem);
                bool canReturn = !CartDisplayHelper.LineLooksFullyReturned(lineItem);

                string refundReason = null;
                if (lineItem.TryGetProperty("refund_reason", out var rr) && rr.ValueKind == JsonValueKind.String)
                    refundReason = rr.GetString();
                else if (lineItem.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String)
                    refundReason = r.GetString();

                Lines.Add(new ReturnSaleLineVm
                {
                    LineId = lineId,
                    Title = title,
                    SubLine = subLine,
                    LineSumText = $"Сумма: {lineTotal} сом",
                    CanReturn = canReturn,
                    RefundReason = refundReason
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
            if (string.IsNullOrEmpty(_currentSaleId)) return;

            var selected = Lines.Where(x => x.CanReturn && x.IsSelected).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Отметьте галочками хотя бы одну позицию для возврата.", "Возврат", MessageBoxButton.OK, MessageBoxImage.Asterisk);
                return;
            }

            decimal total = 0;
            foreach (var line in selected)
            {
                if (decimal.TryParse(line.LineSumText?.Replace("Сумма: ", "").Replace(" сом", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val))
                    total += val;
            }

            if (MessageBox.Show(this, $"Выбрано позиций: {selected.Count}\nСумма возврата: ~{total:F2} сом\n\nПродолжить?",
                "Подтверждение возврата", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var reasonDialog = new ReturnLineReasonDialog(selected.Count) { Owner = this };
            if (reasonDialog.ShowDialog() != true) return;

            string reason = reasonDialog.ReasonText;
            var errors = new List<string>();
            IsBusy = true;

            foreach (var line in selected)
            {
                try
                {
                    await App.Api.PosSaleLineRefundAsync(_currentSaleId, line.LineId, reason).ConfigureAwait(true);
                }
                catch (ApiException ex) { errors.Add($"{line.Title}: {ex.Message}"); }
                catch (HttpRequestException ex) { errors.Add($"{line.Title}: {(string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message)}"); }
                catch (TaskCanceledException) { errors.Add(line.Title + ": превышено время ожидания."); }
            }

            IsBusy = false;

            if (errors.Count == 0)
                MessageBox.Show(this, selected.Count == 1
                    ? "Запрос на возврат отправлен. Проверьте результат в CRM."
                    : $"Запросы на возврат ({selected.Count} поз.) отправлены. Проверьте результат в CRM.",
                    "Возврат", MessageBoxButton.OK, MessageBoxImage.Information);
            else if (errors.Count < selected.Count)
                MessageBox.Show(this, "Часть позиций не удалось вернуть:\n\n" + string.Join("\n", errors),
                    "Возврат", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            else
                MessageBox.Show(this, "Не удалось оформить возврат:\n\n" + string.Join("\n", errors),
                    "Возврат", MessageBoxButton.OK, MessageBoxImage.Error);

            await RefreshCurrentSaleAsync();
        }

        private async Task RefreshCurrentSaleAsync()
        {
            if (string.IsNullOrEmpty(_currentSaleId)) return;
            try
            {
                FillLinesFromSale(await App.Api.PosSaleGetAsync(_currentSaleId).ConfigureAwait(true));
                UpdateReceiptChrome();
            }
            catch { }
            await LoadSalesAsync(true).ConfigureAwait(true);
        }

        private void UpdateReceiptChrome()
        {
            bool hasSale = !string.IsNullOrEmpty(_currentSaleId);
            SelectedChequeBar.Visibility = hasSale ? Visibility.Visible : Visibility.Collapsed;
            ReturnWholeReceiptButton.IsEnabled = hasSale;

            if (!hasSale)
            {
                SelectedSaleText.Text = "";
                LinesPlaceholder.Text = "Сначала выберите чек в списке выше — здесь появится его содержимое.";
                LinesPlaceholder.Visibility = Visibility.Visible;
            }
            else if (Lines.Count == 0)
            {
                SelectedSaleText.Text = "Выбран чек · " + TruncateId(_currentSaleId);
                LinesPlaceholder.Text = "В этом чеке нет позиций с идентификатором строки для возврата через кассу.";
                LinesPlaceholder.Visibility = Visibility.Visible;
            }
            else
            {
                SelectedSaleText.Text = $"Выбран чек · {TruncateId(_currentSaleId)}  ·  позиций в чеке: {Lines.Count}";
                LinesPlaceholder.Visibility = Visibility.Collapsed;
            }
        }

        private static string TruncateId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "—";
            return id.Length <= 36 ? id : id.Substring(0, 32) + "…";
        }

        private async void ReturnWholeReceipt_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentSaleId))
            {
                MessageBox.Show(this, "Сначала выберите чек в списке выше.", "Возврат", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            var reasonDialog = new ReturnLineReasonDialog(kind: ReturnReasonDialogKind.FullReceipt) { Owner = this };
            if (reasonDialog.ShowDialog() != true) return;

            if (MessageBox.Show(this, "Оформить полный возврат всего чека одной операцией? Позиции по отдельности возвращать не потребуется.",
                "Полный возврат", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            IsBusy = true;
            try
            {
                await App.Api.PosSaleRefundOrVoidAsync(_currentSaleId, reasonDialog.ReasonText).ConfigureAwait(true);
                MessageBox.Show(this, "Запрос на полный возврат отправлен. Проверьте результат в CRM.",
                    "Возврат", MessageBoxButton.OK, MessageBoxImage.Information);
                await RefreshCurrentSaleAsync();
            }
            catch (ApiException ex) { MessageBox.Show(this, ex.Message, "Возврат", MessageBoxButton.OK, MessageBoxImage.Error); }
            catch (HttpRequestException ex) { MessageBox.Show(this, string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message, "Возврат", MessageBoxButton.OK, MessageBoxImage.Error); }
            catch (TaskCanceledException) { MessageBox.Show(this, "Превышено время ожидания.", "Возврат", MessageBoxButton.OK, MessageBoxImage.Exclamation); }
            finally { IsBusy = false; }
        }

        private void ShowErr(string msg)
        {
            ErrorText.Text = msg;
            ErrorText.Visibility = Visibility.Visible;
        }

        private void SearchReceiptBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchFilter = SearchReceiptBox.Text;
            _salesViewSource.View.Refresh();
        }

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}