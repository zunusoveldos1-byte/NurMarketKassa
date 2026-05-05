using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using NurMarketKassa.Configuration;
using NurMarketKassa.Services;

#nullable disable

namespace NurMarketKassa.Views
{
    public partial class PosSettingsWindow : Window
    {
        private readonly UpdateService _updateService;

        public PosSettingsWindow()
        {
            InitializeComponent();
            var prefs = UserPreferences.Instance;

            ScaleEnabledCheck.IsChecked = prefs.ScaleEnabled;
            ScaleBaudBox.Text = prefs.ScaleBaudRate.ToString();
            ScaleHexBox.Text = prefs.ScaleRequestHex ?? "";
            ScalePollBox.Text = prefs.ScalePollMs.ToString();

            ReceiptEnabledCheck.IsChecked = prefs.ReceiptEnabled;
            ReceiptLptBox.Text = prefs.ReceiptDevicePath;
            ReceiptEscRBox.Text = prefs.ReceiptEscR?.ToString() ?? "";
            ReceiptRetryBox.Text = prefs.ReceiptRetryCount.ToString();

            FullscreenCheck.IsChecked = prefs.Fullscreen;
            AutostartCheck.IsChecked = prefs.Autostart || AutostartHelper.IsEnabled();
            AutoTouchKeyboardCheck.IsChecked = prefs.AutoShowTouchKeyboard;

            CatalogCardsRadio.IsChecked = prefs.CatalogViewMode == CatalogViewMode.Cards;
            CatalogTableRadio.IsChecked = prefs.CatalogViewMode == CatalogViewMode.Table;
            DoubleClickToCartRadio.IsChecked = !prefs.SingleClickToCart;
            SingleClickToCartRadio.IsChecked = prefs.SingleClickToCart;

            // Заполнение COM‑портов
            var ports = ScaleReaderService.GetAvailablePorts().ToList();
            if (!ports.Contains(prefs.ScaleComPort, StringComparer.OrdinalIgnoreCase))
                ports.Insert(0, prefs.ScaleComPort);
            ScaleComCombo.ItemsSource = ports;
            ScaleComCombo.Text = prefs.ScaleComPort;

            // Кодировка принтера
            SelectComboByTag(ReceiptEncCombo, prefs.ReceiptEncoding.ToLowerInvariant());

            // Таблица кодовой страницы
            string tableTag = prefs.ReceiptEscPosTable?.ToString() ?? "";
            foreach (ComboBoxItem item in ReceiptTableCombo.Items)
            {
                if (item?.Tag?.ToString() == tableTag)
                {
                    ReceiptTableCombo.SelectedItem = item;
                    break;
                }
            }
            if (ReceiptTableCombo.SelectedItem == null && ReceiptTableCombo.Items.Count > 0)
                ReceiptTableCombo.SelectedIndex = 0;

            // Сервис обновлений
            string manifestUrl = App.Settings.Updates.ManifestUrl;
            if (string.IsNullOrWhiteSpace(manifestUrl))
                manifestUrl = Environment.GetEnvironmentVariable("DESKTOP_MARKET_UPDATE_MANIFEST_URL");
            _updateService = new UpdateService(manifestUrl ?? "");

            AppVersionText.Text = "Текущая версия: " +
                (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "неизвестно");
        }

        private void CatalogViewMode_Changed(object sender, RoutedEventArgs e)
        {
            var prefs = UserPreferences.Instance;
            prefs.CatalogViewMode = CatalogTableRadio.IsChecked == true
                ? CatalogViewMode.Table
                : CatalogViewMode.Cards;
            prefs.SaveToDisk();
        }

        private void ClickToCartMode_Changed(object sender, RoutedEventArgs e)
        {
            var prefs = UserPreferences.Instance;
            prefs.SingleClickToCart = SingleClickToCartRadio.IsChecked == true;
            prefs.SaveToDisk();
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn) btn.IsEnabled = false;

            try
            {
                var manifest = await _updateService.CheckAsync();
                if (manifest == null)
                {
                    MessageBox.Show("Обновлений нет или не удалось проверить (проверьте адрес манифеста и соединение).",
                        "Обновление", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (MessageBox.Show($"Доступна новая версия: {manifest.LatestVersion}\nСкачать и установить обновление?",
                    "Обновление", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    await _updateService.DownloadAndRunAsync(manifest.DownloadUrl);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при проверке обновления: " + ex.Message,
                    "Обновление", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (sender is Button b) b.IsEnabled = true;
            }
        }

        private static void SelectComboByTag(ComboBox box, string value)
        {
            foreach (ComboBoxItem item in box.Items)
            {
                if (string.Equals(item?.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedItem = item;
                    return;
                }
            }
            if (box.Items.Count > 0) box.SelectedIndex = 0;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var prefs = UserPreferences.Instance;

            prefs.ScaleEnabled = ScaleEnabledCheck.IsChecked == true;
            prefs.ScaleComPort = HardwarePortHelper.NormalizeComPort(ScaleComCombo.Text);
            if (!int.TryParse(ScaleBaudBox.Text.Trim(), out int baud) || baud <= 0) baud = 9600;
            prefs.ScaleBaudRate = baud;
            prefs.ScaleRequestHex = string.IsNullOrWhiteSpace(ScaleHexBox.Text) ? null : ScaleHexBox.Text.Trim();
            if (!int.TryParse(ScalePollBox.Text.Trim(), out int poll) || poll < 0) poll = 0;
            prefs.ScalePollMs = poll;

            prefs.ReceiptEnabled = ReceiptEnabledCheck.IsChecked == true;
            prefs.ReceiptDevicePath = HardwarePortHelper.NormalizeLptPort(ReceiptLptBox.Text);
            prefs.ReceiptEncoding = (ReceiptEncCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "wpc1251";

            prefs.ReceiptEscPosTable = null;
            if (ReceiptTableCombo.SelectedItem is ComboBoxItem tableItem)
            {
                if (int.TryParse(tableItem.Tag?.ToString(), out int tableByte))
                    prefs.ReceiptEscPosTable = tableByte;
            }
            if (int.TryParse(ReceiptEscRBox.Text.Trim(), out int escR))
                prefs.ReceiptEscR = escR;
            else
                prefs.ReceiptEscR = null;

            if (!int.TryParse(ReceiptRetryBox.Text.Trim(), out int retry) || retry < 1) retry = 3;
            prefs.ReceiptRetryCount = retry;

            // Валидация оборудования
            try
            {
                if (prefs.ScaleEnabled)
                    ScaleReaderService.ValidateSettings(prefs.ToScaleSettings());
                if (prefs.ReceiptEnabled)
                    EscPosTextReceiptPrinter.ValidateSettings(prefs.ToReceiptPrinterSettings());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Настройки кассы", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                return;
            }

            prefs.Fullscreen = FullscreenCheck.IsChecked == true;
            prefs.Autostart = AutostartCheck.IsChecked == true;
            prefs.AutoShowTouchKeyboard = AutoTouchKeyboardCheck.IsChecked == true;

            prefs.SaveToDisk();
            AutostartHelper.SyncFromPreference(prefs.Autostart);

            if (Owner is MainWindow mainWindow)
                mainWindow.ApplyHardwareAndUiPreferences();

            DialogResult = true;
        }

        private void CheckPrinter_Click(object sender, RoutedEventArgs e)
        {
            var cfg = UserPreferences.Instance.ToReceiptPrinterSettings();
            if (string.IsNullOrWhiteSpace(cfg.DevicePath))
            {
                HardwareStatusText.Text = "Не указан порт принтера. Проверьте настройки на вкладке «Печать».";
                return;
            }
            if (!cfg.Enabled)
            {
                HardwareStatusText.Text = "Печать выключена. Включите на вкладке «Печать».";
                return;
            }
            try
            {
                EscPosTextReceiptPrinter.ValidateSettings(cfg);
                EscPosSelfCheckPrinter.PrintSelfCheck(cfg);
                HardwareStatusText.Text = "Тестовая страница отправлена на принтер.";
            }
            catch (Exception ex)
            {
                HardwareStatusText.Text = "Ошибка принтера: " + ex.Message;
            }
        }

        private void CheckScale_Click(object sender, RoutedEventArgs e)
        {
            var prefs = UserPreferences.Instance;
            if (!prefs.ScaleEnabled)
            {
                HardwareStatusText.Text = "Весы выключены. Включите на вкладке «Весы» и укажите COM-порт.";
                return;
            }
            try
            {
                ScaleReaderService.ValidateSettings(prefs.ToScaleSettings());
                using var scale = new ScaleReaderService(prefs.ToScaleSettings());
                scale.Start();
                System.Threading.Thread.Sleep(2000);
                double? weight = scale.LastWeight;
                string status = scale.Status;
                scale.Stop();
                HardwareStatusText.Text = weight.HasValue
                    ? $"Текущий вес: {weight.Value:F3} кг. Статус: {status}."
                    : $"Статус: {status}. Данные не получены. Проверьте подключение.";
            }
            catch (Exception ex)
            {
                HardwareStatusText.Text = "Ошибка весов: " + ex.Message;
            }
        }
    }
}