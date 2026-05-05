using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NurMarketKassa.Services;

#nullable disable

namespace NurMarketKassa.Views
{
    public partial class WeighedProductDialog : Window
    {
        private readonly ScaleReaderService _scale;
        private readonly DispatcherTimer _timer;

        public string QuantityNormalized { get; private set; }

        public WeighedProductDialog(
            string productTitle,
            string pricePerKgLine,
            ScaleReaderService scale,
            string initialKg = null,
            string okButtonText = "В чек",
            string windowTitle = null)
        {
            InitializeComponent();
            _scale = scale;

            if (!string.IsNullOrEmpty(windowTitle))
                Title = windowTitle;

            OkButton.Content = okButtonText;
            TitleBlock.Text = "Взвесить: " + productTitle;
            PriceBlock.Text = string.IsNullOrEmpty(pricePerKgLine) ? "" : "Цена за кг: " + pricePerKgLine;

            if (!string.IsNullOrEmpty(initialKg))
            {
                WeightBox.Text = initialKg;
            }
            else
            {
                double? w = _scale?.LastWeight;
                if (w.HasValue && w.Value > 0)
                    WeightBox.Text = FormatWeight(w.Value);
            }

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += (_, _) => RefreshLiveScale();
            Loaded += (_, _) => _timer.Start();
            Closed += (_, _) => _timer.Stop();
        }

        private void WeightBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (UserPreferences.Instance.AutoShowTouchKeyboard)
                TouchKeyboard.TryShow();
        }

        private void RefreshLiveScale()
        {
            double? w = _scale?.LastWeight;
            LiveScaleText.Text = (!w.HasValue || w.Value <= 0)
                ? "—"
                : FormatWeight(w.Value) + " кг";
        }

        private static string FormatWeight(double w)
        {
            return w.ToString("0.###", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        }

        private void FromScale_Click(object sender, RoutedEventArgs e)
        {
            if (_scale == null)
            {
                MessageBox.Show("Весы не настроены — включите COM в «Настройки кассы».", "Весы",
                    MessageBoxButton.OK, MessageBoxImage.Asterisk);
                return;
            }

            double? w = _scale.LastWeight;
            if (!w.HasValue || w.Value <= 0)
            {
                MessageBox.Show("Нет веса с весов (поставьте товар и подождите).", "Весы",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            WeightBox.Text = FormatWeight(w.Value);
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => TryCloseOk();

        private void WeightBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                TryCloseOk();
            }
        }

        private void TryCloseOk()
        {
            string error = OrderDiscountHelper.ValidateQuantity(WeightBox.Text);
            if (error != null)
            {
                MessageBox.Show(error, "Вес", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            QuantityNormalized = OrderDiscountHelper.NormalizeDecimal(WeightBox.Text);
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}