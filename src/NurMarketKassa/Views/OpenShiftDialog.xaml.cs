using System;
using System.Windows;

namespace NurMarketKassa.Views
{
    public partial class OpenShiftDialog : Window
    {
        /// <summary>Сумма открытия смены (0, если ввод некорректен или пуст).</summary>
        public decimal OpeningCash =>
            decimal.TryParse(OpeningCashBox.Text, out var value) ? value : 0;

        public OpenShiftDialog()
        {
            InitializeComponent();
            OpeningCashBox.Focus();
            OpeningCashBox.SelectAll();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(OpeningCashBox.Text, out _))
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