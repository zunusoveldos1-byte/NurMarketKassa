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

namespace NurMarketKassa.Views.Dialogs {
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

            ApplyLayout();
        }

        private void ApplyLayout()
        {
            if (UserPreferences.Instance.Fullscreen)
            {
                // 1. Переводим окно в полноэкранный режим
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;

                // 2. Перестраиваем ContentGrid на две колонки
                ContentGrid.ColumnDefinitions.Clear();
                ContentGrid.RowDefinitions.Clear();
                ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
                ContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                Grid.SetRow(ChecksBlock, 0);
                Grid.SetColumn(ChecksBlock, 0);

                Grid.SetRow(DetailsBlock, 0);
                Grid.SetColumn(DetailsBlock, 2);
            }
            else
            {
                // 1. Возвращаем окно в обычный режим
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;

                // 2. Перестраиваем ContentGrid на две строки (вертикально)
                ContentGrid.ColumnDefinitions.Clear();
                ContentGrid.RowDefinitions.Clear();
                ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                ContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
                ContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });

                Grid.SetRow(ChecksBlock, 0);
                Grid.SetColumn(ChecksBlock, 0);

                Grid.SetRow(DetailsBlock, 1);
                Grid.SetColumn(DetailsBlock, 0);
            }
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
                var jsonElementList = await App.SalesApi.PosSalesListAsync(page, SalesPageSize, App.PosCashboxId).ConfigureAwait(true);

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
                        // Извлечение даты (поддержка распространённых имён и форматов)
                        DateTime saleDate = DateTime.MinValue;
                        if (TryGetDateTime(row, "date", out saleDate)) { }
                        else if (TryGetDateTime(row, "created_at", out saleDate)) { }
                        else if (TryGetDateTime(row, "sale_date", out saleDate)) { }
                        else if (TryGetDateTime(row, "order_date", out saleDate)) { }

                        // Извлечение суммы (множество возможных полей)
                        // Извлечение суммы
                        decimal totalAmount = 0;
                        if (!TryGetDecimal(row, "total", out totalAmount) &&
                            !TryGetDecimal(row, "grand_total", out totalAmount) &&
                            !TryGetDecimal(row, "total_amount", out totalAmount) &&
                            !TryGetDecimal(row, "amount", out totalAmount) &&
                            !TryGetDecimal(row, "total_sum", out totalAmount) &&
                            !TryGetDecimal(row, "sum", out totalAmount) &&
                            !TryGetDecimal(row, "price", out totalAmount) &&
                            !TryGetDecimal(row, "total_price", out totalAmount) &&
                            !TryGetDecimal(row, "final_total", out totalAmount) &&
                            !TryGetDecimal(row, "order_total", out totalAmount))
                        {
                            // сумма не найдена, totalAmount останется 0
                        }

                        Sales.Add(new ReturnSaleListItemVm
                        {
                            SaleId = saleId,
                            SaleDate = saleDate,
                            TotalAmount = totalAmount,
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
                    PosMessageBox.Show(this, "Больше записей нет.", "Продажи", MessageBoxButton.OK, MessageBoxImage.Asterisk);
                    _salesPage = Math.Max(1, _salesPage - 1);
                }
            }
            catch (ApiException ex)
            {
                if (reset) ShowErr(ex.Message);
                else PosMessageBox.Show(this, ex.Message, "Продажи", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                if (reset) return;
                _salesPage = Math.Max(1, _salesPage - 1);
            }
            catch (HttpRequestException ex)
            {
                string msg = string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message;
                if (reset) ShowErr(msg);
                else PosMessageBox.Show(this, msg, "Продажи", MessageBoxButton.OK, MessageBoxImage.Exclamation);
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

        // Вспомогательные методы (добавьте в класс ReturnSaleDialog)
        private static bool TryGetDateTime(JsonElement element, string propertyName, out DateTime result)
        {
            result = DateTime.MinValue;
            if (element.TryGetProperty(propertyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                    return DateTime.TryParse(prop.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
                else if (prop.ValueKind == JsonValueKind.Null)
                    return false;
            }
            return false;
        }

        private static bool TryGetDecimal(JsonElement element, string propertyName, out decimal result)
        {
            result = 0;
            if (element.TryGetProperty(propertyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                {
                    result = prop.GetDecimal();
                    return true;
                }
                else if (prop.ValueKind == JsonValueKind.String &&
                         decimal.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out result))
                {
                    return true;
                }
            }
            return false;
        }

        private async void SelectSale_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string saleId || string.IsNullOrWhiteSpace(saleId))
                return;

            await OpenSaleByIdAsync(saleId.Trim());
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
                var sale = await App.SalesApi.PosSaleGetAsync(saleId).ConfigureAwait(true);
                _currentSaleId = saleId;
                FillLinesFromSale(sale);
                UpdateReceiptChrome();
                if (Lines.Count == 0)
                    ShowErr("В ответе сервера нет позиций с идентификатором строки для возврата.");
                // IsSaleSelected = true;   ← удаляем эту строку
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
                string lineId = CartDisplayHelper.TryRefundLineId(lineItem);
                if (string.IsNullOrEmpty(lineId)) continue;

                string title = CartDisplayHelper.ItemName(lineItem);
                string subLine = CartDisplayHelper.QuantityPriceLine(lineItem);
                string lineTotal = CartDisplayHelper.LineTotal(lineItem);
                double originalQty = CartDisplayHelper.LineQuantity(lineItem);
                double qty = CartDisplayHelper.RefundableQuantity(lineItem);
                bool canReturn = qty > 0 && !CartDisplayHelper.LineLooksFullyReturned(lineItem);

                string refundReason = null;
                if (lineItem.TryGetProperty("refund_reason", out var rr) && rr.ValueKind == JsonValueKind.String)
                    refundReason = rr.GetString();
                else if (lineItem.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String)
                    refundReason = r.GetString();

                Lines.Add(new ReturnSaleLineVm
                {
                    LineId = lineId,
                    ProductId = CartDisplayHelper.TryProductId(lineItem),
                    Quantity = qty,
                    OriginalQuantity = originalQty,
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
                PosMessageBox.Show(this, "Отметьте галочками хотя бы одну позицию для возврата.", "Возврат", MessageBoxButton.OK, MessageBoxImage.Asterisk);
                return;
            }

            decimal total = 0;
            foreach (var line in selected)
            {
                if (decimal.TryParse(line.LineSumText?.Replace("Сумма: ", "").Replace(" сом", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val))
                    total += val;
            }

            if (PosMessageBox.Show(this, $"Выбрано позиций: {selected.Count}\nСумма возврата: ~{total:F2} сом\n\nПродолжить?",
                "Подтверждение возврата", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var reasonDialog = new ReturnLineReasonDialog(selected.Count) { Owner = this };
            if (reasonDialog.ShowDialog() != true) return;

            string reason = reasonDialog.ReasonText;
            IsBusy = true;

            try
            {
                var requests = selected.Select(line => new PosRefundLineRequest
                {
                    LineId = line.LineId,
                    ProductId = line.ProductId,
                    Title = line.Title,
                    Quantity = line.Quantity > 0 ? line.Quantity : 1,
                    OriginalQuantity = line.OriginalQuantity > 0 ? line.OriginalQuantity : line.Quantity,
                }).ToList();

                await PosRefundService.RefundLinesAsync(
                    App.SalesApi,
                    _currentSaleId,
                    requests,
                    reason,
                    App.PosCashboxId).ConfigureAwait(true);

                PosMessageBox.Show(this, selected.Count == 1
                    ? "Возврат оформлен."
                    : $"Возврат оформлен ({selected.Count} поз.).",
                    "Возврат", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (ApiException ex) when (ex.StatusCode == 502 && ex.Message.Contains("Возвращено позиций", StringComparison.Ordinal))
            {
                PosMessageBox.Show(this, ex.Message, "Возврат", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (ApiException ex)
            {
                PosMessageBox.Show(this, ex.Message, "Возврат", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (HttpRequestException ex)
            {
                PosMessageBox.Show(this, string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message,
                    "Возврат", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (TaskCanceledException)
            {
                PosMessageBox.Show(this, "Превышено время ожидания.", "Возврат", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
            finally
            {
                IsBusy = false;
            }

            await RefreshCurrentSaleAsync();
        }

        private async Task RefreshCurrentSaleAsync()
        {
            if (string.IsNullOrEmpty(_currentSaleId)) return;
            try
            {
                FillLinesFromSale(await App.SalesApi.PosSaleGetAsync(_currentSaleId).ConfigureAwait(true));
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
                PosMessageBox.Show(this, "Сначала выберите чек в списке выше.", "Возврат", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            var reasonDialog = new ReturnLineReasonDialog(kind: ReturnReasonDialogKind.FullReceipt) { Owner = this };
            if (reasonDialog.ShowDialog() != true) return;

            if (PosMessageBox.Show(this, "Оформить полный возврат всего чека одной операцией? Позиции по отдельности возвращать не потребуется.",
                "Полный возврат", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            IsBusy = true;
            try
            {
                await PosRefundService.RefundWholeSaleAsync(
                    App.SalesApi,
                    _currentSaleId,
                    reasonDialog.ReasonText,
                    App.PosCashboxId).ConfigureAwait(true);
                PosMessageBox.Show(this, "Полный возврат чека оформлен.",
                    "Возврат", MessageBoxButton.OK, MessageBoxImage.Information);
                await RefreshCurrentSaleAsync();
            }
            catch (ApiException ex) { PosMessageBox.Show(this, ex.Message, "Возврат", MessageBoxButton.OK, MessageBoxImage.Error); }
            catch (HttpRequestException ex) { PosMessageBox.Show(this, string.IsNullOrWhiteSpace(ex.Message) ? "Нет подключения." : ex.Message, "Возврат", MessageBoxButton.OK, MessageBoxImage.Error); }
            catch (TaskCanceledException) { PosMessageBox.Show(this, "Превышено время ожидания.", "Возврат", MessageBoxButton.OK, MessageBoxImage.Exclamation); }
            finally { IsBusy = false; }
        }

        private void Header_Click(object sender, RoutedEventArgs e)
        {
            if (sender is GridViewColumnHeader header && header.Content is string headerText)
            {
                string sortProperty = headerText switch
                {
                    "Номер чека" => "SaleId",
                    "Дата" => "SaleDate",
                    "Сумма" => "TotalAmount",
                    _ => null
                };
                if (sortProperty == null) return;

                var view = SalesView;
                using (view.DeferRefresh())
                {
                    view.SortDescriptions.Clear();
                    view.SortDescriptions.Add(new SortDescription(sortProperty, ListSortDirection.Ascending));
                }
            }
        }

        private void SalesListView_Loaded(object sender, RoutedEventArgs e) => AdjustColumnWidths();
        private void SalesListView_SizeChanged(object sender, SizeChangedEventArgs e) => AdjustColumnWidths();

        private void AdjustColumnWidths()
        {
            if (SalesListView.View is not GridView gridView) return;

            // Фиксированная ширина колонки с кнопкой
            const double buttonColumnWidth = 120;
            // Пропорции для трёх текстовых колонок: Номер : Дата : Сумма = 2 : 1 : 1
            double totalProportion = 4; // 2+1+1
            double availableWidth = SalesListView.ActualWidth - buttonColumnWidth - SystemParameters.VerticalScrollBarWidth - 8; // небольшой запас

            if (availableWidth <= 0) return;

            // Предполагаем, что колонки идут в порядке: Номер, Дата, Сумма
            if (gridView.Columns.Count >= 3)
            {
                gridView.Columns[0].Width = availableWidth * (2.0 / totalProportion); // Номер чека
                gridView.Columns[1].Width = availableWidth * (1.0 / totalProportion); // Дата
                gridView.Columns[2].Width = availableWidth * (1.0 / totalProportion); // Сумма
            }
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