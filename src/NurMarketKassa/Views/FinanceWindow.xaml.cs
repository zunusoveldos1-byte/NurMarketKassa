using NurMarketKassa.Services;
using NurMarketKassa.Views.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

#nullable disable

namespace NurMarketKassa.Views
{
    public partial class FinanceWindow : Window, INotifyPropertyChanged
    {
        // ── внутренние коллекции ──
        private readonly ObservableCollection<SaleItem> _sales = new();
        private readonly ObservableCollection<RefundItem> _refunds = new();
        private readonly ObservableCollection<HistoryItem> _history = new();
        private ObservableCollection<CashSessionEntry> _cashSessions;
        private readonly CollectionViewSource _salesViewSource = new();
        private readonly CollectionViewSource _historyViewSource = new();
        private DateTime _historyFrom = DateTime.Today;
        private DateTime _historyTo = DateTime.Today;
        private bool _isLoading;
        private string _errorMessage; 
        private CancellationTokenSource _loadCts;
        private DispatcherTimer _clockTimer;
        private string _currentUserId;
        private bool _isHamburgerOpen;

        private static readonly string CashHistoryFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NurMarketKassa",
            "cash_history.json");

        // ── публичные свойства ──
        public ObservableCollection<TopItem> TopItems { get; } = new();
        public ObservableCollection<CashSessionEntry> CashSessions => _cashSessions;
        public ICollectionView SalesView => _salesViewSource.View;
        public ICollectionView RefundsView => CollectionViewSource.GetDefaultView(_refunds);
        public ICollectionView HistoryView => _historyViewSource.View;

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotLoading)); }
        }

        public bool IsNotLoading => !IsLoading;

        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
        }

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        public FinanceWindow()
        {
            InitializeComponent();
            _currentUserId = App.CurrentUserId;

            _salesViewSource.Source = _sales;
            _salesViewSource.Filter += FilterSales;
            _historyViewSource.Source = _history;
            _historyViewSource.Filter += FilterHistory;

            DataContext = this;

            if (UserPreferences.Instance.Fullscreen)
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }

            var cashHistory = LoadCashHistoryFromDisk();
            _cashSessions = new ObservableCollection<CashSessionEntry>(cashHistory);
            OnPropertyChanged(nameof(CashSessions));

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => CurrentTimeText.Text = DateTime.Now.ToString("HH:mm:ss   dd.MM.yyyy");
            _clockTimer.Start();
        }

        private void FilterSales(object sender, FilterEventArgs e)
        {
            e.Accepted = true;
        }

        private void FilterHistory(object sender, FilterEventArgs e)
        {
            e.Accepted = true;
        }

        public class TopItem
        {
            public string ProductName { get; set; }
            public decimal Revenue { get; set; }
            public int Quantity { get; set; }
        }

        protected override void OnClosed(EventArgs e)
        {
            _clockTimer?.Stop();
            base.OnClosed(e);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FromPicker.SelectedDate = _historyFrom;
            ToPicker.SelectedDate = _historyTo;
            CustomDatePill.IsChecked = false;
            CustomDatePanel.Visibility = Visibility.Collapsed;
            await LoadDataAsync(_historyFrom, _historyTo);
        }
        

        private void Window_ManipulationBoundaryFeedback(object sender, ManipulationBoundaryFeedbackEventArgs e)
        {
            e.Handled = true; // Подавляем "подпрыгивание" окна при сенсорном скролле
        }

        private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is DataGrid grid)
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(grid);
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3);
                    e.Handled = true;
                }
            }
        }

        //private void MainScrollViewer_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        //{
        //    _targetScrollViewer = null;
            

        //    // Определяем, на каком элементе начался жест
        //    var source = e.OriginalSource as DependencyObject;

        //    // Ищем DataGrid под пальцем
        //    var grid = FindVisualParent<DataGrid>(source);
        //    if (grid != null)
        //    {
        //        // Если один палец – будем скроллить таблицу
        //        if (e.Manipulators.Count() == 1)
        //        {
        //            _targetScrollViewer = FindVisualChild<ScrollViewer>(grid);
        //        }
        //        else // два или больше пальцев – скроллим страницу
        //        {
        //            _targetScrollViewer = MainScrollViewer;
        //        }
        //    }
        //    else
        //    {
        //        // Жест вне таблицы – всегда скроллим страницу
        //        _targetScrollViewer = MainScrollViewer;
        //    }

        //    // Разрешаем только вертикальное перемещение
        //    if (_targetScrollViewer != null)
        //    {
        //        e.Mode = ManipulationModes.TranslateY;
        //        e.Handled = true;
        //    }
        //}

        //private void MainScrollViewer_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        //{
        //    if (_targetScrollViewer == null) return;

        //    var deltaY = e.DeltaManipulation.Translation.Y;
        //    _targetScrollViewer.ScrollToVerticalOffset(
        //        _targetScrollViewer.VerticalOffset - deltaY);
        //    e.Handled = true;
        //}

        //private void MainScrollViewer_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        //{
        //    _targetScrollViewer = null;
        //}

        // Вспомогательные методы
        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild) return typedChild;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T typed) return typed;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private void HamburgerMenu_Click(object sender, RoutedEventArgs e)
        {
            _isHamburgerOpen = !_isHamburgerOpen;
            AnimateHamburgerMenu(_isHamburgerOpen);
        }

        private void HamburgerOverlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isHamburgerOpen)
            {
                _isHamburgerOpen = false;
                AnimateHamburgerMenu(false);
            }
        }

        private void HamburgerMenuClose_Click(object sender, RoutedEventArgs e)
        {
            _isHamburgerOpen = false;
            AnimateHamburgerMenu(false);
        }

        private void ShowUnderConstruction()
        {
            MainContent.Visibility = Visibility.Collapsed;
            UnderConstructionPanel.Visibility = Visibility.Visible;
        }

        private void HideUnderConstruction()
        {
            UnderConstructionPanel.Visibility = Visibility.Collapsed;
            MainContent.Visibility = Visibility.Visible;
        }

        private void UnderConstructionBack_Click(object sender, RoutedEventArgs e)
        {
            HideUnderConstruction();
        }

        private void AnimateHamburgerMenu(bool open)
        {
            double from = open ? -320 : 0;
            double to = open ? 0 : -320;

            var animation = new ThicknessAnimation
            {
                From = new Thickness(from, 0, 0, 0),
                To = new Thickness(to, 0, 0, 0),
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new QuadraticEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn }
            };

            if (open) HamburgerOverlay.Visibility = Visibility.Visible;

            animation.Completed += (s, args) =>
            {
                if (!open) HamburgerOverlay.Visibility = Visibility.Collapsed;
            };

            HamburgerPanel.BeginAnimation(MarginProperty, animation);
        }

        // Закрываем меню при выборе пункта
        private void CloseHamburgerMenu()
        {
            if (_isHamburgerOpen)
            {
                _isHamburgerOpen = false;
                AnimateHamburgerMenu(false);
            }
        }

        private void NavigateToSale_Click(object sender, RoutedEventArgs e)
        {
            CloseHamburgerMenu();
            var salesWindow = new SalesWindow { Owner = this };
            salesWindow.ShowDialog();
            Close();
        }

        private void NavigateToStock_Click(object sender, RoutedEventArgs e)
        {
            CloseHamburgerMenu();
            ShowUnderConstruction();
        }

        private void ManageShift_Click(object sender, RoutedEventArgs e)
        {
            CloseHamburgerMenu();
            ShowUnderConstruction();
        }

        private void NavigateToProducts_Click(object sender, RoutedEventArgs e)
        {
            CloseHamburgerMenu();
            ShowUnderConstruction();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            CloseHamburgerMenu();
            Close();
        }

        private async Task LoadDataAsync(DateTime from, DateTime to)
        {
            CancelLoad();
            _loadCts = new CancellationTokenSource();
            var token = _loadCts.Token;

            try
            {
                IsLoading = true;
                ErrorMessage = null;

                var allItems = await FetchSalesPageAsync(1, 500, token);
                var filtered = allItems
                    .Where(s => s.CreatedAt.Date >= from.Date && s.CreatedAt.Date <= to.Date)
                    .ToList();

                var sales = filtered.Where(s => !s.IsRefund).ToList();
                var refunds = filtered.Where(s => s.IsRefund).ToList();

                UpdateCollections(sales, refunds);
                UpdateStats(sales, refunds);
                UpdateHistory(sales, refunds);
                await LoadTopItemsAsync(sales, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ErrorMessage = "Ошибка загрузки: " + ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadTopItemsAsync(List<SaleItem> sales, CancellationToken token)
        {
            var dict = new Dictionary<string, (decimal revenue, int qty)>();
            foreach (var sale in sales)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var json = await App.SalesApi.PosSaleGetAsync(sale.Id, CancellationToken.None);
                    // парсим элементы чека
                    if (json.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var line in items.EnumerateArray())
                        {
                            string name = line.TryGetProperty("product_name", out var n) ? n.GetString() ?? "?" : "?";
                            decimal price = line.TryGetProperty("price", out var p) ? p.GetDecimal() : 0;
                            decimal qty = line.TryGetProperty("quantity", out var q) ? q.GetDecimal() : 0;
                            decimal total = price * qty;
                            if (dict.ContainsKey(name))
                                dict[name] = (dict[name].revenue + total, dict[name].qty + (int)qty);
                            else
                                dict[name] = (total, (int)qty);
                        }
                    }
                }
                catch { /* пропускаем ошибки загрузки чека */ }
            }

            var top10 = dict.OrderByDescending(kv => kv.Value.revenue).Take(10);
            TopItems.Clear();
            foreach (var kv in top10)
                TopItems.Add(new TopItem { ProductName = kv.Key, Revenue = kv.Value.revenue, Quantity = kv.Value.qty });
        }

        private async Task<List<SaleItem>> FetchSalesPageAsync(int page, int pageSize, CancellationToken token)
        {
            var raw = await App.SalesApi.PosSalesListAsync(page, pageSize, null, CancellationToken.None);
            token.ThrowIfCancellationRequested();

            var result = new List<SaleItem>(raw.Count);
            foreach (JsonElement el in raw)
            {
                var item = new SaleItem();
                if (el.TryGetProperty("id", out var idProp))
                    item.Id = idProp.ToString() ?? "";
                if (el.TryGetProperty("created_at", out var dateProp) &&
                    DateTime.TryParse(dateProp.GetString(), out var dt))
                    item.CreatedAt = dt;
                if (el.TryGetProperty("receipt_number", out var rnProp))
                    item.ReceiptNumber = rnProp.GetString() ?? "";
                if (el.TryGetProperty("total", out var totalProp))
                    item.TotalAmount = ParseDecimal(totalProp);
                if (el.TryGetProperty("payment_method", out var pmProp))
                    item.PaymentMethod = pmProp.GetString() ?? "";
                if (el.TryGetProperty("is_refund", out var rfProp) && rfProp.GetBoolean())
                    item.IsRefund = true;
                if (el.TryGetProperty("refund_reason", out var rrProp))
                    item.RefundReason = rrProp.GetString();
                if (el.TryGetProperty("customer_id", out var cidProp))
                    item.CustomerId = cidProp.GetString();
                if (string.IsNullOrWhiteSpace(item.ReceiptNumber))
                    item.ReceiptNumber = item.Id;
                result.Add(item);
            }
            return result;
        }

        private static decimal ParseDecimal(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var d))
                return d;
            if (element.ValueKind == JsonValueKind.String &&
                decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d2))
                return d2;
            return 0m;
        }

        private void CancelLoad()
        {
            if (_loadCts != null)
            {
                _loadCts.Cancel();
                _loadCts.Dispose();
                _loadCts = null;
            }
        }

        private void UpdateCollections(List<SaleItem> sales, List<SaleItem> refunds)
        {
            _sales.Clear();
            foreach (var s in sales) _sales.Add(s);

            _refunds.Clear();
            foreach (var r in refunds)
            {
                _refunds.Add(new RefundItem
                {
                    Id = r.Id,
                    CreatedAt = r.CreatedAt,
                    ReceiptNumber = r.ReceiptNumber,
                    TotalAmount = Math.Abs(r.TotalAmount),
                    Reason = r.RefundReason ?? "—"
                });
            }
        }

        private void UpdateStats(List<SaleItem> sales, List<SaleItem> refunds)
        {
            decimal totalSales = sales.Sum(s => s.TotalAmount);
            decimal totalRefunds = refunds.Sum(r => Math.Abs(r.TotalAmount));

            decimal cashSales = sales
                .Where(s =>
                    (s.PaymentMethod?.ToUpper() == "CASH") ||
                    (s.PaymentMethod?.ToLower().Contains("нал") ?? false))
                .Sum(s => s.TotalAmount);

            decimal nonCash = totalSales - cashSales;
            int totalCount = sales.Count + refunds.Count;
            decimal net = totalSales - totalRefunds;
            decimal avg = totalCount > 0 ? net / totalCount : 0m;

            // ── Базовые показатели ──
            TotalSalesText.Text = $"{totalSales:N2} сом";
            TotalRefundsText.Text = $"{totalRefunds:N2} сом";
            CashText.Text = $"{cashSales:N2} сом";
            NonCashText.Text = $"{nonCash:N2} сом";
            AvgReceiptText.Text = $"{avg:N2} сом";
            ReceiptCountText.Text = totalCount.ToString();

            // ── Новые показатели ──
            // Чистая прибыль и маржа (пока условно: себестоимость = 60% выручки)
            decimal costOfGoods = totalSales * 0.6m;
            decimal netProfit = totalSales - costOfGoods;
            if (NetProfitText != null)
                NetProfitText.Text = $"{netProfit:N2} сом";

            if (MarginPercentText != null)
            {
                decimal margin = totalSales > 0 ? (netProfit / totalSales * 100) : 0;
                MarginPercentText.Text = $"{margin:F1}%";
            }

            // Максимальный, минимальный чек и долг временно скрыты —
            // их можно вернуть, добавив соответствующие TextBlock в XAML
        }

        private void UpdateHistory(List<SaleItem> sales, List<SaleItem> refunds)
        {
            _history.Clear();
            foreach (var s in sales)
            {
                _history.Add(new HistoryItem
                {
                    Id = s.Id,
                    CreatedAt = s.CreatedAt,
                    Type = "Продажа",
                    ReceiptNumber = s.ReceiptNumber ?? s.Id ?? "—",
                    TotalAmount = s.TotalAmount,
                    PaymentMethod = s.PaymentMethod ?? "—"
                });
            }
            foreach (var r in refunds)
            {
                _history.Add(new HistoryItem
                {
                    Id = r.Id,
                    CreatedAt = r.CreatedAt,
                    Type = "Возврат",
                    ReceiptNumber = r.ReceiptNumber ?? r.Id ?? "—",
                    TotalAmount = -Math.Abs(r.TotalAmount),
                    PaymentMethod = "—"
                });
            }
            // Обновляем представление, чтобы фильтр применился заново
            _historyViewSource.View.Refresh();
        }

        // Детали чека (popup)
        private async Task ShowReceiptDetailsByIdAsync(string receiptId, string receiptNumber)
        {
            if (string.IsNullOrEmpty(receiptId)) return;
            try
            {
                var json = await App.SalesApi.PosSaleGetAsync(receiptId, CancellationToken.None);
                var items = new List<string>();
                try
                {
                    foreach (var line in CartDisplayHelper.EnumerateSaleLineItems(json))
                    {
                        items.Add($"• {CartDisplayHelper.ItemName(line)} — " +
                                  $"{CartDisplayHelper.LineQuantity(line)} × " +
                                  $"{CartDisplayHelper.UnitPrice(line):N2} = " +
                                  $"{CartDisplayHelper.LineTotal(line):N2}");
                    }
                }
                catch
                {
                    if (json.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var line in arr.EnumerateArray())
                        {
                            string name = line.TryGetProperty("product_name", out var n) ? n.GetString() ?? "?" : "?";
                            decimal qty = line.TryGetProperty("quantity", out var q) ? q.GetDecimal() : 0;
                            decimal price = line.TryGetProperty("price", out var p) ? p.GetDecimal() : 0;
                            decimal total = line.TryGetProperty("total", out var t) ? t.GetDecimal() : 0;
                            items.Add($"• {name} — {qty} × {price:N2} = {total:N2}");
                        }
                    }
                }
                ShowReceiptDetailsPopup(receiptNumber ?? "—", items);
            }
            catch (Exception ex)
            {
                ErrorMessage = "Не удалось загрузить детали чека: " + ex.Message;
            }
        }

        private void ShowReceiptDetailsPopup(string receiptNumber, List<string> items)
        {
            if (ReceiptDetailsPopup == null || PopupTitle == null || PopupItemsControl == null)
            {
                // Элементы ещё не загружены — ничего не делаем
                return;
            }

            PopupTitle.Text = "Чек " + receiptNumber;
            PopupItemsControl.ItemsSource = items;
            ReceiptDetailsPopup.IsOpen = true;
        }

        private void CloseReceiptDetails_Click(object sender, RoutedEventArgs e) =>
            ReceiptDetailsPopup.IsOpen = false;

        private async void SalesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SalesGrid.SelectedItem is SaleItem item)
                await ShowReceiptDetailsByIdAsync(item.Id, item.ReceiptNumber);
        }

        private async void RefundsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (RefundsGrid.SelectedItem is RefundItem item)
                await ShowReceiptDetailsByIdAsync(item.Id, item.ReceiptNumber);
        }

        private async void HistoryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (HistoryGrid.SelectedItem is HistoryItem item)
                await ShowReceiptDetailsByIdAsync(item.Id, item.ReceiptNumber);
        }

        // Быстрые даты
        private async void QuickDate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb || rb.Tag is not string tag) return;

            if (tag == "Custom")
            {
                var dlg = new FinanceDateRangeDialog { Owner = this };
                dlg.ShowDialog();
                CustomDatePill.IsChecked = false;
                return;
            }

            DateTime from, to;
            DateTime today = DateTime.Today;
            switch (tag)
            {
                case "Yesterday": from = to = today.AddDays(-1); break;
                case "Week": from = today.AddDays(-(int)today.DayOfWeek); to = today; break;
                case "Month": from = new DateTime(today.Year, today.Month, 1); to = today; break;
                default: from = to = today; break;
            }

            _historyFrom = from;
            _historyTo = to;
            FromPicker.SelectedDate = from;
            ToPicker.SelectedDate = to;
            await LoadDataAsync(from, to);
            CustomDatePill.IsChecked = false;
        }

        private void DatePicker_DateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender == FromPicker)
                _historyFrom = FromPicker.SelectedDate ?? DateTime.Today;
            else if (sender == ToPicker)
                _historyTo = ToPicker.SelectedDate ?? DateTime.Today;
        }

        // Работа с кассовой историей
        private static List<CashSessionEntry> LoadCashHistoryFromDisk()
        {
            try
            {
                if (File.Exists(CashHistoryFilePath))
                    return JsonSerializer.Deserialize<List<CashSessionEntry>>(File.ReadAllText(CashHistoryFilePath))
                           ?? new List<CashSessionEntry>();
            }
            catch { }
            return new List<CashSessionEntry>();
        }

        private static void SaveCashHistoryToDisk(IEnumerable<CashSessionEntry> entries)
        {
            try
            {
                var dir = Path.GetDirectoryName(CashHistoryFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(CashHistoryFilePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = false }));
            }
            catch { }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) =>
            await LoadDataAsync(_historyFrom, _historyTo);

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // Вложенные классы
        public class SaleItem
        {
            public string Id { get; set; } = "";
            public DateTime CreatedAt { get; set; }
            public string ReceiptNumber { get; set; } = "";
            public decimal TotalAmount { get; set; }
            public string PaymentMethod { get; set; } = "";
            public bool IsRefund { get; set; }
            public string RefundReason { get; set; }
            public string CustomerId { get; set; }
        }

        public class RefundItem
        {
            public string Id { get; set; } = "";
            public DateTime CreatedAt { get; set; }
            public string ReceiptNumber { get; set; } = "";
            public decimal TotalAmount { get; set; }
            public string Reason { get; set; } = "";
        }

        public class HistoryItem
        {
            public string Id { get; set; } = "";
            public DateTime CreatedAt { get; set; }
            public string Type { get; set; } = "";
            public string ReceiptNumber { get; set; } = "";
            public decimal TotalAmount { get; set; }
            public string PaymentMethod { get; set; } = "";
        }

        public class CashSessionEntry
        {
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public decimal Amount { get; set; }
            public string UserId { get; set; }
            public string Type { get; set; }
            public string Comment { get; set; }
        }
    }
}