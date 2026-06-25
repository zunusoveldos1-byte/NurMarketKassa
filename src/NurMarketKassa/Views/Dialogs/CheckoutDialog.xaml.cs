using NurMarketKassa.Services;
using NurMarketKassa.Services.Hardware;
using NurMarketKassa.ViewModels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;

namespace NurMarketKassa.Views.Dialogs {
    public partial class CheckoutDialog : Window
    {
        private readonly CheckoutViewModel _viewModel;

        public string PaymentMethodKey => _viewModel.PaymentMethod;

        public bool IsPrintReceiptEnabled => _viewModel.IsPrintReceiptEnabled;

        public string CashReceivedForApi
        {
            get
            {
                if (_viewModel.PaymentMethod == "cash")
                {
                    var normalized = CheckoutValidation.NormalizeDecimal(_viewModel.CashReceived);
                    var total = _viewModel.EffectiveTotalDue;
                    return string.IsNullOrEmpty(normalized)
                        ? total.ToString("0.00", CultureInfo.InvariantCulture)
                        : normalized;
                }

                return "0.00";
            }
        }

        public Dictionary<string, string>? PendingOrderDiscountBody => _viewModel.PendingOrderDiscountBody;

        public CheckoutDialog(
            CartTotalsCalculator.CartTotals totals,
            string orderDiscountPercent,
            string orderDiscountSum)
        {
            InitializeComponent();
            _viewModel = new CheckoutViewModel(totals, orderDiscountPercent, orderDiscountSum);
            DataContext = _viewModel;

            _viewModel.RequestClose += result =>
            {
                if (!result)
                {
                    DialogResult = false;
                    return;
                }

                if (_viewModel.IsPrintReceiptEnabled && !HardwareModeHelper.IsPrinterPortConfigured())
                {
                    var decision = PosDialogs.ShowPrinterNotConnected(this);
                    if (decision == PrinterNotConnectedResult.Cancel)
                        return;

                    _viewModel.IsPrintReceiptEnabled = false;
                }

                if (!PaymentConfirmationDialog.Show(this))
                    return;

                DialogResult = true;
            };

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PosDialogLayout.FitOverlayToOwner(this, Owner);
            PosModalHost.PlayOpenAnimation(RootGrid, DialogCard);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            PosDialogLayout.FitOverlayToOwner(this, Owner);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
