using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NurMarketKassa.Configuration;
using NurMarketKassa.Services;

#nullable disable

namespace NurMarketKassa.Views.Dialogs {
    public partial class CashOperationsDialog : Window
    {
        private readonly ObservableCollection<CashOperationEntry> _allOperations;
        private string _editingOperationId; // <-- добавлено

        public ObservableCollection<CashOperationEntry> AllOperations => _allOperations; // <-- добавлено

        private static readonly string HistoryFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NurMarketKassa",
            "cash_history.json");

        // Делегаты для открытия/закрытия смены, устанавливаются из MainWindow
        public Func<decimal, Task> OpenShiftAction { get; set; }
        public Func<decimal?, Task> CloseShiftAction { get; set; }

        public CashOperationsDialog()
        {
            InitializeComponent();
            DataContext = this;              // <-- добавлено
            _allOperations = new ObservableCollection<CashOperationEntry>(LoadHistoryFromFile());
            HistoryGrid.ItemsSource = _allOperations;

            // Полноэкранный режим
            if (UserPreferences.Instance.Fullscreen)
            {
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
                FullscreenExitButton.Visibility = Visibility.Visible;
            }
            else
            {
                FullscreenExitButton.Visibility = Visibility.Collapsed;
            }

            // Подписка на изменение состояния смены
            ShiftService.ShiftStateChanged += OnShiftStateChanged;
            UpdateShiftStatus(ShiftService.IsShiftOpen);

            // Горячие клавиши
            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) Close();
                else if (e.Key == Key.Enter && SaveButton.IsEnabled)
                    Save_Click(null, null);
            };

            Loaded += (s, e) => AmountBox.Focus();
        }

        private void OnShiftStateChanged(bool isOpen)
        {
            Dispatcher.Invoke(() => UpdateShiftStatus(isOpen));
        }

        private void UpdateShiftStatus(bool isOpen)
        {
            if (isOpen)
            {
                ShiftStatusText.Text = "ОТКРЫТА";
                ShiftStatusText.Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                OperationPanel.IsEnabled = true;
                AmountPanel.IsEnabled = true;
                CommentPanel.IsEnabled = true;
                ActionPanel.IsEnabled = true;
                OpenShiftButton2.IsEnabled = false;
                CloseShiftButton2.IsEnabled = true;
                ShiftActionHint.Text = "Смена открыта. Можно выполнять операции с наличными.";
            }
            else
            {
                ShiftStatusText.Text = "ЗАКРЫТА";
                ShiftStatusText.Foreground = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                OperationPanel.IsEnabled = false;
                AmountPanel.IsEnabled = false;
                CommentPanel.IsEnabled = false;
                ActionPanel.IsEnabled = false;
                OpenShiftButton2.IsEnabled = true;
                CloseShiftButton2.IsEnabled = false;
                ShiftActionHint.Text = "Смена закрыта. Откройте смену, чтобы начать операции.";
                ErrorText.Text = "Смена закрыта. Операции с наличными недоступны.";
            }
        }

        private async void OpenShift_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenShiftDialog();
            if (PosDialogHost.Show(dlg, this) == true)
            {
                if (OpenShiftAction != null)
                {
                    try
                    {
                        OpenShiftButton2.IsEnabled = false;
                        await OpenShiftAction(dlg.OpeningCash);
                        // Добавляем запись начального остатка
                        var entry = new CashOperationEntry
                        {
                            CreatedAt = DateTime.Now,
                            Type = "Начальный остаток",
                            Amount = dlg.OpeningCash,
                            Comment = "Смена открыта",
                            UserId = App.CurrentUserId
                        };
                        _allOperations.Insert(0, entry);
                        SaveHistoryToFile(_allOperations);
                        UpdateBalance();
                    }
                    catch (Exception ex)
                    {
                        ErrorText.Text = "Ошибка открытия смены: " + ex.Message;
                    }
                    finally
                    {
                        OpenShiftButton2.IsEnabled = !ShiftService.IsShiftOpen;
                    }
                }
            }
        }

        private async void CloseShift_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CloseShiftDialog();
            if (PosDialogHost.Show(dlg, this) == true)
            {
                if (CloseShiftAction != null)
                {
                    try
                    {
                        CloseShiftButton2.IsEnabled = false;
                        await CloseShiftAction(dlg.ClosingCash);
                    }
                    catch (Exception ex)
                    {
                        ErrorText.Text = "Ошибка закрытия смены: " + ex.Message;
                    }
                    finally
                    {
                        CloseShiftButton2.IsEnabled = ShiftService.IsShiftOpen;
                    }
                }
            }
        }

        private void FullscreenExit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void AmountBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !char.IsDigit(e.Text[0]) && e.Text != "." && e.Text != ",";
        }

        private void QuickAmount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                if (decimal.TryParse(AmountBox.Text, out decimal current) &&
                    decimal.TryParse(btn.Tag.ToString(), out decimal add))
                {
                    AmountBox.Text = (current + add).ToString("0");
                }
                else
                {
                    AmountBox.Text = btn.Tag.ToString();
                }
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = "";
            string amountText = AmountBox.Text.Trim();
            if (string.IsNullOrEmpty(amountText))
            {
                ErrorText.Text = "Введите сумму.";
                AmountBox.Focus();
                return;
            }
            if (!decimal.TryParse(amountText, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal amount) || amount <= 0m)
            {
                ErrorText.Text = "Некорректная сумма.";
                AmountBox.Focus();
                return;
            }

            bool isDeposit = DepositRadio.IsChecked.GetValueOrDefault();
            string type = isDeposit ? "Внесение" : "Изъятие";

            if (!isDeposit)
            {
                decimal currentBalance = CalculateBalance();
                if (amount > currentBalance)
                {
                    ErrorText.Text = $"Недостаточно средств в кассе. Доступно: {currentBalance:N2} сом.";
                    AmountBox.Focus();
                    return;
                }
            }

            string comment = CommentBox.Text.Trim();

            // Редактирование существующей операции
            if (_editingOperationId != null)
            {
                var op = _allOperations.FirstOrDefault(o => o.Id == _editingOperationId);
                if (op != null)
                {
                    op.Type = type;
                    op.Amount = amount;
                    op.Comment = comment;
                    UpdateBalance();
                    SaveHistoryToFile(_allOperations);
                    if (PrintCheckBox.IsChecked == true)
                        PrintCashOperation(op);
                }
                _editingOperationId = null;
                SaveButton.Content = "Сохранить операцию";
            }
            else
            {
                // Создание новой операции
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

                if (PrintCheckBox.IsChecked == true)
                    PrintCashOperation(entry);
            }

            // Очистка полей
            AmountBox.Text = "";
            CommentBox.Text = "";
            ErrorText.Text = "";
            DepositRadio.IsChecked = true;
            AmountBox.Focus();
            UpdateBalance();
        }

        private void UndoLast_Click(object sender, RoutedEventArgs e)
        {
            if (_allOperations.Count == 0)
            {
                ErrorText.Text = "История пуста.";
                return;
            }
            var last = _allOperations[0];
            if (last.Type == "Начальный остаток")
            {
                ErrorText.Text = "Нельзя удалить начальный остаток.";
                return;
            }
            string message = $"Удалить последнюю операцию?\n\n{last.CreatedAt:HH:mm:ss} — {last.Type} {last.Amount:N2} сом";
            if (PosMessageBox.Show(message, "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _allOperations.RemoveAt(0);
                SaveHistoryToFile(_allOperations);
                UpdateBalance();
                ErrorText.Text = "Операция удалена.";
            }
        }

        private void UpdateBalance()
        {
            decimal balance = CalculateBalance();
            CurrentBalanceText.Text = $"{balance:N2} сом";
        }

        private decimal CalculateBalance()
        {
            decimal balance = 0;
            foreach (var op in _allOperations)
            {
                if (op.Type == "Внесение" || op.Type == "Начальный остаток")
                    balance += op.Amount;
                else if (op.Type == "Изъятие")
                    balance -= op.Amount;
            }
            return balance;
        }

        private void PrintCashOperation(CashOperationEntry entry)
        {
            PosMessageBox.Show($"Печать ордера:\n{entry.Type}: {entry.Amount:N2} сом\nКомментарий: {entry.Comment}",
                            "Печать", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private static List<CashOperationEntry> LoadHistoryFromFile()
        {
            try
            {
                if (File.Exists(HistoryFilePath))
                {
                    string json = File.ReadAllText(HistoryFilePath);
                    return JsonSerializer.Deserialize<List<CashOperationEntry>>(json) ?? new List<CashOperationEntry>();
                }
            }
            catch { }
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
            catch { }
        }

        // Обработчик нажатия кнопки "Изменить"
        private void EditOperation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CashOperationEntry entry)
            {
                if (entry.Type == "Внесение")
                    DepositRadio.IsChecked = true;
                else if (entry.Type == "Изъятие")
                    WithdrawRadio.IsChecked = true;

                AmountBox.Text = entry.Amount.ToString("0.00", CultureInfo.InvariantCulture);
                CommentBox.Text = entry.Comment;
                _editingOperationId = entry.Id;
                SaveButton.Content = "Обновить операцию";
            }
        }

        // Обработчик нажатия кнопки "Удалить"
        private void DeleteOperation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CashOperationEntry entry)
            {
                var confirm = PosMessageBox.Show(
                    $"Удалить операцию «{entry.Type}» на сумму {entry.Amount:0.00} сом?",
                    "Подтверждение удаления",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm == MessageBoxResult.Yes)
                {
                    _allOperations.Remove(entry);
                    SaveHistoryToFile(_allOperations);
                    ErrorText.Text = "";
                }
            }
        }

        public class CashOperationEntry
        {
            public string Id { get; set; } = Guid.NewGuid().ToString(); // <-- добавлено
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public string Type { get; set; } = "";
            public decimal Amount { get; set; }
            public string Comment { get; set; } = "";
            public string UserId { get; set; } = "";
        }
    }
}