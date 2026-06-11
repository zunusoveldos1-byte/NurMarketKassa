using System.Globalization;
using System.Windows;
using NurMarketKassa.Services;

namespace NurMarketKassa.Views
{
    public partial class CloseShiftDialog : Window
    {
        public decimal? ClosingCash =>
            decimal.TryParse(ClosingCashBox.Text, out var value) ? value : null;

        public decimal? SuggestedBalance { get; set; }

        public CloseShiftDialog()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (SuggestedBalance.HasValue)
                {
                    SystemBalanceText.Text = ShiftBalanceHelper.FormatBalance(SuggestedBalance);
                    ClosingCashBox.Text = SuggestedBalance.Value.ToString("0.00", CultureInfo.InvariantCulture);
                }
                ClosingCashBox.Focus();
                ClosingCashBox.SelectAll();
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(ClosingCashBox.Text) &&
                !decimal.TryParse(ClosingCashBox.Text, out _))
            {
                MessageBox.Show("Введите корректную сумму.", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
