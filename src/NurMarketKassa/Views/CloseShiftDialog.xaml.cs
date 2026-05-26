using System;
using System.Windows;

namespace NurMarketKassa.Views
{
    public partial class CloseShiftDialog : Window
    {
        /// <summary>Остаток в кассе (null, если поле пустое или некорректное).</summary>
        public decimal? ClosingCash =>
            decimal.TryParse(ClosingCashBox.Text, out var value) ? value : null;

        public CloseShiftDialog()
        {
            InitializeComponent();
            ClosingCashBox.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(ClosingCashBox.Text) &&
                !decimal.TryParse(ClosingCashBox.Text, out _))
            {
                MessageBox.Show(
                    "Введите корректную сумму.",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}