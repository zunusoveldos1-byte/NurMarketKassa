using System;
using System.Windows;
using System.Windows.Controls;
using NurMarketKassa.Services;

#nullable disable

namespace NurMarketKassa.Views
{
    public partial class OrderDiscountDialog : Window
    {
        public string DiscountMode { get; private set; } = "percent";
        public string DiscountValue { get; private set; } = "";
        public bool ClearRequested { get; private set; }

        public OrderDiscountDialog(string currentPercent, string currentSum)
        {
            InitializeComponent();

            string pct = (currentPercent ?? "").Trim();
            string sum = (currentSum ?? "").Trim();

            CurrentDiscountText.Text = !string.IsNullOrEmpty(pct)
                ? $"Сейчас действует скидка: {pct}%"
                : !string.IsNullOrEmpty(sum)
                    ? $"Сейчас действует скидка: {sum} сом"
                    : "Сейчас скидка на чек не задана.";

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

            Loaded += (_, _) =>
            {
                ValueBox.Focus();
                ValueBox.SelectAll();
            };
        }

        private void DiscountType_Changed(object sender, RoutedEventArgs e) => SyncModeUi();

        private void SyncModeUi()
        {
            if (RbPercent == null || RbSum == null || ValueLabel == null)
                return;

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
            string error = DiscountMode == "percent"
                ? OrderDiscountHelper.ValidatePercent(raw)
                : OrderDiscountHelper.ValidateSum(raw);

            if (error != null)
            {
                ErrorText.Text = error;
                ErrorText.Visibility = Visibility.Visible;
            }
            else
            {
                DiscountValue = OrderDiscountHelper.NormalizeDecimal(raw);
                ClearRequested = false;
                DialogResult = true;
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            ClearRequested = true;
            DiscountValue = "";
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}