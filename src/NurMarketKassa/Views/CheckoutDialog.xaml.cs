using NurMarketKassa.Services;
using NurMarketKassa.ViewModels;
using System;
using System.Globalization;
using System.Windows;

namespace NurMarketKassa.Views
{
    public partial class CheckoutDialog : Window
    {
        private readonly CheckoutViewModel _viewModel;
        private readonly double _totalDue;
        private bool _printReceiptAsked;

        public string PaymentMethodKey => _viewModel.PaymentMethod;
        public string CashReceivedForApi
        {
            get
            {
                if (_viewModel.PaymentMethod == "cash")
                {
                    string normalized = CheckoutValidation.NormalizeDecimal(_viewModel.CashReceived);
                    return string.IsNullOrEmpty(normalized)
                        ? _totalDue.ToString("0.00", CultureInfo.InvariantCulture)
                        : normalized;
                }
                return "0.00";
            }
        }
        public bool WantPrintReceipt => _viewModel.IsPrintReceiptEnabled;

        public CheckoutDialog(double totalDue)
        {
            _totalDue = totalDue;
            InitializeComponent();
            _viewModel = new CheckoutViewModel(totalDue);
            DataContext = _viewModel;

            _viewModel.RequestClose += result =>
            {
                if (result)
                {
                    // Спросить подтверждение оплаты
                    var confirmResult = MessageBox.Show(
                        "Подтвердить оплату?",
                        "Подтверждение",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (confirmResult != MessageBoxResult.Yes)
                        return;

                    // Спросить про печать чека, если ещё не спросили
                    if (!_printReceiptAsked)
                    {
                        var printResult = MessageBox.Show(
                            "Хотите напечатать чек?",
                            "Печать",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        _viewModel.IsPrintReceiptEnabled = (printResult == MessageBoxResult.Yes);
                    }

                    DialogResult = true;
                }
                else
                {
                    DialogResult = false;
                }
            };

            // Сразу при открытии спросить про печать
            AskPrintReceipt();
        }

        private void AskPrintReceipt()
        {
            var printResult = MessageBox.Show(
                "Хотите напечатать чек?",
                "Печать чека",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            _viewModel.IsPrintReceiptEnabled = (printResult == MessageBoxResult.Yes);
            _printReceiptAsked = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}