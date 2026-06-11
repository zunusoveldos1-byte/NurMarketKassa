using System.Globalization;
using System.Windows;
using NurMarketKassa.Services;

namespace NurMarketKassa.Views
{
    public partial class OpenShiftDialog : Window
    {
        public decimal OpeningCash =>
            decimal.TryParse(OpeningCashBox.Text, out var value) ? value : 0;

        public decimal? SuggestedBalance { get; set; }

        public OpenShiftDialog()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                if (SuggestedBalance.HasValue)
                {
                    SystemBalanceText.Text = ShiftBalanceHelper.FormatBalance(SuggestedBalance);
                    OpeningCashBox.Text = SuggestedBalance.Value.ToString("0.00", CultureInfo.InvariantCulture);
                }
                OpeningCashBox.Focus();
                OpeningCashBox.SelectAll();
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(OpeningCashBox.Text, out _))
            {
                MessageBox.Show("??????? ?????????? ?????.", "??????",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
