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

            // Показываем панель весов и кнопку подстановки, только если есть сервис
            bool hasScale = _scale != null;
            ScalePanel.Visibility = hasScale ? Visibility.Visible : Visibility.Collapsed;
            FromScaleButton.Visibility = hasScale ? Visibility.Visible : Visibility.Collapsed;
            ManualHintText.Visibility = hasScale ? Visibility.Collapsed : Visibility.Visible;

            // Устанавливаем начальное значение
            if (!string.IsNullOrEmpty(initialKg))
            {
                WeightBox.Text = initialKg;
            }
            else if (hasScale)
            {
                double? w = _scale.LastWeight;
                if (w.HasValue && w.Value > 0)
                    WeightBox.Text = FormatWeight(w.Value);
            }

            // Таймер обновления показаний (если есть весы)
            if (hasScale)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _timer.Tick += (_, _) => RefreshLiveScale();
                Loaded += (_, _) => _timer.Start();
                Closed += (_, _) => _timer.Stop();
            }

            // Автофокус на поле ввода
            Loaded += (_, _) => WeightBox.Focus();
        }

        private void WeightBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Разрешаем только цифры, запятую и точку (для вставки тоже работает)
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c) && c != '.' && c != ',')
                {
                    e.Handled = true;
                    return;
                }
            }
            // Запрещаем второй разделитель
            string current = WeightBox.Text ?? "";
            if ((e.Text.Contains('.') || e.Text.Contains(',')) && (current.Contains('.') || current.Contains(',')))
            {
                e.Handled = true;
                return;
            }
        }

        private void WeightBox_GotFocus(object sender, RoutedEventArgs e)
        {
            WeightBox.SelectAll();
            if (UserPreferences.Instance.AutoShowTouchKeyboard)
                TouchKeyboard.TryShow();
        }

        private void Numpad_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                string ch = btn.Tag.ToString()!;
                // Запрещаем второй разделитель
                if ((ch == "." || ch == ",") && (WeightBox.Text.Contains('.') || WeightBox.Text.Contains(',')))
                    return;
                WeightBox.Text += ch;
                WeightBox.CaretIndex = WeightBox.Text.Length;
                WeightBox.Focus();
            }
        }

        private void NumpadBackspace_Click(object sender, RoutedEventArgs e)
        {
            string current = WeightBox.Text;
            if (current.Length > 0)
            {
                WeightBox.Text = current.Substring(0, current.Length - 1);
                WeightBox.CaretIndex = WeightBox.Text.Length;
                WeightBox.Focus();
            }
        }

        private void RefreshLiveScale()
        {
            if (_scale == null) return;
            double? w = _scale.LastWeight;
            LiveScaleText.Text = (w.HasValue && w.Value > 0)
                ? FormatWeight(w.Value) + " кг"
                : "—";
        }

        private static string FormatWeight(double w)
        {
            return w.ToString("0.###", CultureInfo.InvariantCulture)
                  .TrimEnd('0')
                  .TrimEnd('.');
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
                MessageBox.Show("Нет веса с весов (поставьте товар на весы и подождите стабилизации).", "Весы",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            WeightBox.Text = FormatWeight(w.Value);
            WeightBox.SelectAll();
        }

        private void QuickWeight_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                WeightBox.Text = btn.Tag.ToString().Replace(',', '.');
                WeightBox.SelectAll();
            }
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
            string raw = (WeightBox.Text ?? "").Trim();
            if (raw.Length == 0)
            {
                MessageBox.Show("Введите вес.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                WeightBox.Focus();
                return;
            }

            // Нормализуем запятую в точку
            raw = raw.Replace(',', '.');
            if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal kg) || kg <= 0)
            {
                MessageBox.Show("Вес должен быть положительным числом.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                WeightBox.Focus();
                return;
            }

            QuantityNormalized = kg.ToString("0.###", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
            if (string.IsNullOrEmpty(QuantityNormalized)) QuantityNormalized = "0";

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}