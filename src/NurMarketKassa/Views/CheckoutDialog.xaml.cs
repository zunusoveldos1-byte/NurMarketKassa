using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NurMarketKassa.Services;

#nullable disable

namespace NurMarketKassa.Views
{
    public partial class CheckoutDialog : Window
    {
        private readonly double _totalDue;

        public string PaymentMethodKey { get; private set; } = "cash";
        public string CashReceivedForApi { get; private set; } = "";
        public bool WantPrintReceipt { get; private set; }

        public CheckoutDialog(double totalDue)
        {
            _totalDue = totalDue;
            InitializeComponent();

            TotalLabel.Text = $"К оплате: {totalDue.ToString("0.00", CultureInfo.InvariantCulture)} сом";
            CashReceivedBox.Text = totalDue.ToString("0.00", CultureInfo.InvariantCulture);

            SyncCashFieldVisibility();
            UpdateChangeDisplay();

            CashReceivedBox.Focus();
            CashReceivedBox.SelectAll();
        }

        private void PayMethod_Changed(object sender, RoutedEventArgs e) =>
            SyncCashFieldVisibility();

        private void CashReceived_TextChanged(object sender, TextChangedEventArgs e) =>
            UpdateChangeDisplay();

        private void SyncCashFieldVisibility()
        {
            if (CashReceivedBox == null || RbCash == null)
                return;

            bool isCash = RbCash.IsChecked.GetValueOrDefault();
            CashReceivedBox.IsEnabled = isCash;
            CashReceivedBox.Opacity = isCash ? 1.0 : 0.5;
            UpdateChangeDisplay();
        }

        private void UpdateChangeDisplay()
        {
            if (ChangeDisplayText == null || CashReceivedBox == null || RbCash == null)
                return;

            if (!RbCash.IsChecked.GetValueOrDefault())
            {
                ChangeDisplayText.Visibility = Visibility.Collapsed;
                return;
            }

            string normalized = CheckoutValidation.NormalizeDecimal(CashReceivedBox.Text);
            if (string.IsNullOrEmpty(normalized) || !double.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out double cash))
            {
                ChangeDisplayText.Visibility = Visibility.Collapsed;
                return;
            }

            if (cash > _totalDue + 1E-09)
            {
                double change = cash - _totalDue;
                ChangeDisplayText.Text = $"Сдача: {change.ToString("0.00", CultureInfo.InvariantCulture)} сом";
                ChangeDisplayText.Visibility = Visibility.Visible;
            }
            else
            {
                ChangeDisplayText.Visibility = Visibility.Collapsed;
            }
        }

        private void Pay_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            ErrorText.Text = "";

            PaymentMethodKey = RbTransfer.IsChecked.GetValueOrDefault() ? "transfer" : "cash";
            WantPrintReceipt = true;

            if (PaymentMethodKey == "cash")
            {
                string error = CheckoutValidation.ValidateCashReceived(CashReceivedBox.Text, _totalDue);
                if (error != null)
                {
                    ErrorText.Text = error;
                    ErrorText.Visibility = Visibility.Visible;
                    return;
                }

                string normalized = CheckoutValidation.NormalizeDecimal(CashReceivedBox.Text);
                CashReceivedForApi = string.IsNullOrEmpty(normalized)
                    ? _totalDue.ToString("0.00", CultureInfo.InvariantCulture)
                    : normalized;
            }
            else
            {
                CashReceivedForApi = "0.00";
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) =>
            DialogResult = false;
    }
}