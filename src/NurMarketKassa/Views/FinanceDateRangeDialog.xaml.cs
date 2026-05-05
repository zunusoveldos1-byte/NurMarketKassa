using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

#nullable disable

namespace NurMarketKassa.Views
{
    public partial class FinanceDateRangeDialog : Window
    {
        private readonly ObservableCollection<SaleItem> _sales = new ObservableCollection<SaleItem>();
        private CancellationTokenSource _loadCts;

        public FinanceDateRangeDialog()
        {
            InitializeComponent();
            FromDatePicker.SelectedDate = DateTime.Today;
            ToDatePicker.SelectedDate = DateTime.Today;
            SalesGrid.ItemsSource = _sales;
        }

        private async void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (FromDatePicker.SelectedDate == null || ToDatePicker.SelectedDate == null)
            {
                ErrorText.Text = "Выберите обе даты.";
                return;
            }

            DateTime from = FromDatePicker.SelectedDate.Value;
            DateTime to = ToDatePicker.SelectedDate.Value;

            if (from > to)
            {
                ErrorText.Text = "Дата «От» не может быть позже даты «До».";
                return;
            }

            ErrorText.Text = "";
            await LoadDataAsync(from, to);
        }

        private async Task LoadDataAsync(DateTime from, DateTime to)
        {
            CancelLoad();
            _loadCts = new CancellationTokenSource();
            CancellationToken token = _loadCts.Token;

            try
            {
                // Загружаем максимум 200 продаж (одна страница API)
                var allItems = await FetchSalesPageAsync(1, 200, token);
                var filtered = allItems
                    .Where(s => s.CreatedAt.Date >= from.Date && s.CreatedAt.Date <= to.Date)
                    .ToList();

                _sales.Clear();
                foreach (var item in filtered)
                    _sales.Add(item);
            }
            catch (OperationCanceledException)
            {
                // отмена загрузки — ничего не делаем
            }
            catch (Exception ex)
            {
                ErrorText.Text = "Ошибка загрузки: " + ex.Message;
            }
            finally
            {
                _loadCts = null;
            }
        }

        private async Task<List<SaleItem>> FetchSalesPageAsync(int page, int pageSize, CancellationToken token)
        {
            List<JsonElement> rawList = await App.Api.PosSalesListAsync(page, pageSize, null, CancellationToken.None);
            token.ThrowIfCancellationRequested();

            var result = new List<SaleItem>(rawList.Count);
            foreach (JsonElement el in rawList)
            {
                var item = new SaleItem();

                if (el.TryGetProperty("id", out JsonElement idProp))
                    item.Id = idProp.ToString() ?? "";

                if (el.TryGetProperty("created_at", out JsonElement dateProp) &&
                    DateTime.TryParse(dateProp.GetString(), out DateTime dt))
                    item.CreatedAt = dt;

                if (el.TryGetProperty("receipt_number", out JsonElement rnProp))
                    item.ReceiptNumber = rnProp.GetString() ?? "";

                if (el.TryGetProperty("total", out JsonElement totalProp))
                    item.TotalAmount = ParseDecimal(totalProp);

                if (el.TryGetProperty("payment_method", out JsonElement pmProp))
                    item.PaymentMethod = pmProp.GetString() ?? "";

                if (string.IsNullOrWhiteSpace(item.ReceiptNumber))
                    item.ReceiptNumber = item.Id;

                result.Add(item);
            }

            return result;
        }

        private static decimal ParseDecimal(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out decimal d))
                return d;
            if (element.ValueKind == JsonValueKind.String &&
                decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d2))
                return d2;
            return 0m;
        }

        private async void SalesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SalesGrid.SelectedItem is SaleItem selected)
            {
                try
                {
                    await App.Api.PosSaleGetAsync(selected.Id, CancellationToken.None);
                    MessageBox.Show("Детали чека " + selected.ReceiptNumber, "Информация");
                }
                catch (Exception ex)
                {
                    ErrorText.Text = "Ошибка загрузки деталей: " + ex.Message;
                }
            }
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

        public class SaleItem
        {
            public string Id { get; set; } = "";
            public DateTime CreatedAt { get; set; }
            public string ReceiptNumber { get; set; } = "";
            public decimal TotalAmount { get; set; }
            public string PaymentMethod { get; set; } = "";
        }
    }
}