using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

#nullable enable

namespace NurMarketKassa.Views.Dialogs {
    public partial class OrderDiscountDialog : Window
    {
        // Возвращаемые значения
        public string DiscountMode { get; private set; } = "percent";
        public string DiscountScope { get; private set; } = "check"; // "check" или "item"
        public string DiscountValue { get; private set; } = "";
        public bool ClearRequested { get; private set; }

        public OrderDiscountDialog(string currentPercent, string currentSum)
        {
            InitializeComponent();

            string pct = (currentPercent ?? "").Trim();
            string sum = (currentSum ?? "").Trim();

            // Логика начального состояния (можно расширить)
            if (!string.IsNullOrEmpty(sum))
            {
                RbSum.IsChecked = true;
                ValueBox.Text = sum;
            }
            else
            {
                RbPercent.IsChecked = true;
                ValueBox.Text = pct;
            }

            SyncModeUi();
        }

        public void SetItemMode(string itemTitle, string? currentDiscountType, decimal? currentDiscountValue)
        {
            Title = "Скидка на товар";
            HeaderTitleText.Text = "Скидка на товар";
            ItemTitleLabel.Text = itemTitle;
            ItemTitleLabel.Visibility = Visibility.Visible;
            ScopePanel.Visibility = Visibility.Collapsed;   // скрываем выбор «Весь чек / Выбранный товар»
            RbItem.IsChecked = true;
            DiscountScope = "item";

            // Предзаполняем текущими значениями
            if (currentDiscountValue.HasValue && currentDiscountType != null)
            {
                if (currentDiscountType == "percent")
                {
                    RbPercent.IsChecked = true;
                    ValueBox.Text = currentDiscountValue.Value.ToString("F0", CultureInfo.InvariantCulture);
                }
                else if (currentDiscountType == "sum")
                {
                    RbSum.IsChecked = true;
                    ValueBox.Text = currentDiscountValue.Value.ToString("F2", CultureInfo.InvariantCulture);
                }
            }
            else
            {
                ValueBox.Text = "";
            }
            SyncModeUi();
        }

        private void DiscountType_Changed(object sender, RoutedEventArgs e) => SyncModeUi();

        private void SyncModeUi()
        {
            if (RbPercent == null || ValueLabel == null) return;

            bool isPercent = RbPercent.IsChecked == true;
            DiscountMode = isPercent ? "percent" : "sum";
            ValueLabel.Text = isPercent ? "Введите процент скидки" : "Введите сумму скидки";
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            ErrorText.Text = "";
            SyncModeUi();

            string raw = (ValueBox.Text ?? "").Trim();
            string? error = null;

            if (decimal.TryParse(raw, out decimal val))
            {
                if (DiscountMode == "percent")
                {
                    if (val < 0 || val > 100) error = "Процент должен быть от 0 до 100";
                }
                else
                {
                    if (val < 0) error = "Сумма не может быть отрицательной";
                }
            }
            else
            {
                error = "Введите корректное число";
            }

            if (error != null)
            {
                ErrorText.Text = error;
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            DiscountValue = val.ToString(System.Globalization.CultureInfo.InvariantCulture);
            // <<< Удалена строка: DiscountScope = RbCheck.IsChecked == true ? "check" : "item";
            // DiscountScope уже установлен: "check" (конструктор) или "item" (SetItemMode)
            ClearRequested = false;
            DialogResult = true;
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            ClearRequested = true;
            DiscountValue = "";
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }
    }
}