using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

#nullable disable

namespace NurMarketKassa.Views
{
    public partial class CashOperationsDialog : Window
    {
        private readonly ObservableCollection<CashOperationEntry> _allOperations;

        private static readonly string HistoryFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NurMarketKassa",
            "cash_operations_history.json");

        public CashOperationsDialog()
        {
            InitializeComponent();
            _allOperations = new ObservableCollection<CashOperationEntry>(LoadHistoryFromFile());
            HistoryItemsControl.ItemsSource = _allOperations;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string amountText = AmountBox.Text.Trim();
            if (string.IsNullOrEmpty(amountText))
            {
                ErrorText.Text = "Введите сумму.";
                return;
            }

            if (!decimal.TryParse(amountText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amount) || amount <= 0m)
            {
                ErrorText.Text = "Некорректная сумма.";
                return;
            }

            string type = DepositRadio.IsChecked.GetValueOrDefault() ? "Внесение" : "Изъятие";
            string comment = CommentBox.Text.Trim();

            var entry = new CashOperationEntry
            {
                CreatedAt = DateTime.Now,
                Type = type,
                Amount = amount,
                Comment = comment,
                UserId = App.CurrentUserId
            };

            _allOperations.Insert(0, entry);
            SaveHistoryToFile(_allOperations);

            AmountBox.Text = "";
            CommentBox.Text = "";
            ErrorText.Text = "";
            DepositRadio.IsChecked = true;
        }

        private static List<CashOperationEntry> LoadHistoryFromFile()
        {
            try
            {
                if (File.Exists(HistoryFilePath))
                {
                    string json = File.ReadAllText(HistoryFilePath);
                    return JsonSerializer.Deserialize<List<CashOperationEntry>>(json)
                           ?? new List<CashOperationEntry>();
                }
            }
            catch
            {
                // ignore
            }
            return new List<CashOperationEntry>();
        }

        private static void SaveHistoryToFile(IEnumerable<CashOperationEntry> entries)
        {
            try
            {
                string dir = Path.GetDirectoryName(HistoryFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = false });
                File.WriteAllText(HistoryFilePath, json);
            }
            catch
            {
                // ignore
            }
        }

        public class CashOperationEntry
        {
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public string Type { get; set; } = "";
            public decimal Amount { get; set; }
            public string Comment { get; set; } = "";
            public string UserId { get; set; }
        }
    }
}