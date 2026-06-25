using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NurMarketKassa.Configuration;
using NurMarketKassa.Services;
using NurMarketKassa.Services.Hardware;
using NurMarketKassa.Views.Dialogs;

#nullable disable

namespace NurMarketKassa.Views.Dialogs {
    public partial class WeighedProductDialog : Window
    {
        private readonly IWeightScaleService _scale;
        private readonly bool _scaleLive;
        private readonly DispatcherTimer _timer;
        private double? _lastLoggedWeight;

        public string QuantityNormalized { get; private set; }

        public WeighedProductDialog(
            string productTitle,
            string pricePerKgLine,
            IWeightScaleService scale,
            string initialKg = null,
            string okButtonText = "В чек",
            string windowTitle = null)
        {
            InitializeComponent();
            _scale = scale;
            _scaleLive = HasLiveScaleConnection(scale);

            var title = string.IsNullOrEmpty(windowTitle) ? "Взвесить товар" : windowTitle;
            Title = title;
            HeaderTitleText.Text = title;

            OkButton.Content = okButtonText;
            TitleBlock.Text = "Взвесить: " + productTitle;
            PriceBlock.Text = string.IsNullOrEmpty(pricePerKgLine) ? "" : "Цена за кг: " + pricePerKgLine;

            var scaleConfigured = HardwareModeHelper.UsePhysicalScale() && scale != null;
            ScalePanel.Visibility = scaleConfigured ? Visibility.Visible : Visibility.Collapsed;
            FromScaleButton.Visibility = _scaleLive ? Visibility.Visible : Visibility.Collapsed;
            ManualHintText.Visibility = _scaleLive ? Visibility.Collapsed : Visibility.Visible;

            // Режим редактирования — оставляем переданный вес; новое взвешивание всегда с нуля.
            if (!string.IsNullOrEmpty(initialKg))
            {
                WeightBox.Text = initialKg;
            }
            else
            {
                WeightBox.Text = "0.00";
            }

            LiveScaleText.Text = FormatLiveScaleText();

            if (scaleConfigured)
            {
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _timer.Tick += (_, _) => RefreshLiveScale();
                Loaded += (_, _) => _timer.Start();
                Closed += (_, _) => _timer.Stop();
            }

            Loaded += OnDialogLoaded;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            PosDialogLayout.FitOverlayToOwner(this, Owner);
        }

        private void OnDialogLoaded(object sender, RoutedEventArgs e)
        {
            PosDialogLayout.FitOverlayToOwner(this, Owner);
            PosModalHost.PlayOpenAnimation(RootGrid, DialogCard);
            WeightBox.Focus();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => CloseWithResult(false);

        private void CloseWithResult(bool? result)
        {
            if (result == null)
                return;

            PosModalHost.PlayCloseAnimation(this, () =>
            {
                DialogResult = result;
                Close();
            });
        }

        private static bool HasLiveScaleConnection(IWeightScaleService scale)
        {
            if (!HardwareModeHelper.UsePhysicalScale())
                return false;

            if (scale is VirtualWeightScaleService)
                return false;

            return scale is ComWeightScaleService { IsAvailable: true };
        }

        private string FormatLiveScaleText()
        {
            if (!_scaleLive)
                return "—";

            if (_scale?.LastWeight is double w)
                return w.ToString("0.00", CultureInfo.InvariantCulture) + " кг";

            return "0.00 кг";
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
                TouchKeyboard.TryShow(this);
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
            var weight = _scaleLive ? _scale?.LastWeight : null;
            if (weight != _lastLoggedWeight)
            {
                _lastLoggedWeight = weight;
                PosLogger.Log(
                    $"UI весов: «На весах сейчас» = {FormatLiveScaleText()} (LastWeight={weight?.ToString("0.###", CultureInfo.InvariantCulture) ?? "null"})",
                    "SCALE_UI");
            }

            LiveScaleText.Text = FormatLiveScaleText();
        }

        private static string FormatWeight(double w)
        {
            return w.ToString("0.###", CultureInfo.InvariantCulture)
                  .TrimEnd('0')
                  .TrimEnd('.');
        }

        private void FromScale_Click(object sender, RoutedEventArgs e)
        {
            if (_scale == null || !_scaleLive)
            {
                PosMessageBox.Show("Весы не подключены или порт недоступен — укажите вес вручную.", "Весы",
                    MessageBoxButton.OK, MessageBoxImage.Asterisk);
                return;
            }

            if (_scale.LastWeight is not double w || w <= 0)
            {
                PosMessageBox.Show("Нет веса с весов (поставьте товар на весы и подождите стабилизации).", "Весы",
                    MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            PosLogger.Log($"UI весов: «Подставить с весов» = {w.ToString("0.###", CultureInfo.InvariantCulture)} кг", "SCALE_UI");
            WeightBox.Text = FormatWeight(w);
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
                PosMessageBox.Show("Введите вес.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                WeightBox.Focus();
                return;
            }

            // Нормализуем запятую в точку
            raw = raw.Replace(',', '.');
            if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal kg) || kg <= 0)
            {
                PosMessageBox.Show("Вес должен быть положительным числом.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                WeightBox.Focus();
                return;
            }

            QuantityNormalized = JsonNumericReader.FormatWeightForApi((double)kg);
            if (string.IsNullOrEmpty(QuantityNormalized)) QuantityNormalized = "0";

            CloseWithResult(true);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => CloseWithResult(false);
    }
}