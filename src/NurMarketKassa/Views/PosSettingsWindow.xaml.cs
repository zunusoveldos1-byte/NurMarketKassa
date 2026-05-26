using NurMarketKassa.Configuration;
using NurMarketKassa.Models;
using NurMarketKassa.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

#nullable enable

namespace NurMarketKassa.Views
{
    public partial class PosSettingsWindow : Window
    {
        private readonly UpdateService _updateService;

        private string? _recommendedEncoding;
        private string? _recommendedEscTable;

        private ObservableCollection<BankQrSetting> _bankSettings;
        private readonly string[] _banks = { "Элкарт", "MBank", "ФинкаБанк" };
        private readonly Dictionary<string, string> _logoMap = new()
        {
    { "Элкарт",   "pack://application:,,,/Assets/Elkart-logo.png" },
    { "MBank",    "pack://application:,,,/Assets/Mbank-logo.png" },
    { "ФинкаБанк", "pack://application:,,,/Assets/Finca-logo.png" }
        };
        public PosSettingsWindow()
        {
            InitializeComponent();

            _bankSettings = new ObservableCollection<BankQrSetting>();  // ← инициализация
            LoadBankQrSettings();  // теперь безопасно

            if (UserPreferences.Instance.Fullscreen)
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }

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

            DoubleClickToCartRadio.IsChecked = !prefs.SingleClickToCart;
            SingleClickToCartRadio.IsChecked = prefs.SingleClickToCart;

            var ports = ScaleReaderService.GetAvailablePorts().ToList();
            if (!ports.Contains(prefs.ScaleComPort, StringComparer.OrdinalIgnoreCase))
                ports.Insert(0, prefs.ScaleComPort);
            ScaleComCombo.ItemsSource = ports;
            ScaleComCombo.Text = prefs.ScaleComPort;

            SelectComboByTag(ReceiptEncCombo, prefs.ReceiptEncoding.ToLowerInvariant());
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

            string? manifestUrl = App.Settings.Updates.ManifestUrl;
            if (string.IsNullOrWhiteSpace(manifestUrl))
                manifestUrl = Environment.GetEnvironmentVariable("DESKTOP_MARKET_UPDATE_MANIFEST_URL");
            _updateService = new UpdateService(manifestUrl ?? "");
            AppVersionText.Text = "Текущая версия: " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "неизвестно");
        }

        private void ClickToCartMode_Changed(object sender, RoutedEventArgs e)
        {
            var prefs = UserPreferences.Instance;
            prefs.SingleClickToCart = SingleClickToCartRadio.IsChecked == true;
            prefs.SaveToDisk();
        }

        private void LoadBankQrSettings()
        {
            _bankSettings.Clear();
            var prefs = UserPreferences.Instance;
            foreach (var bank in _banks)
            {
                string? qrPath = prefs.BankQrPaths?.TryGetValue(bank, out var qr) == true ? qr : null;

                _bankSettings.Add(new BankQrSetting
                {
                    BankName = bank,
                    LogoPath = _logoMap[bank],          // фиксированный из Assets
                    QrCodePath = qrPath
                });
            }
            BankQrItemsControl.ItemsSource = _bankSettings;
        }
        private void LoadQrCode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BankQrSetting setting)
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp",
                    Title = $"Выберите QR-код для банка {setting.BankName}"
                };
                if (dlg.ShowDialog() == true)
                {
                    setting.QrCodePath = dlg.FileName; // сохраняем путь к файлу
                    SaveBankQrSettings();
                }
            }
        }

        private void SaveBankQrSettings()
        {
            var prefs = UserPreferences.Instance;
            prefs.BankQrPaths ??= new Dictionary<string, string>();
            prefs.BankQrPaths.Clear();
            foreach (var bs in _bankSettings)
            {
                if (!string.IsNullOrEmpty(bs.QrCodePath))
                    prefs.BankQrPaths[bs.BankName] = bs.QrCodePath;
            }
            prefs.SaveToDisk();  // ← обязательно сохраняем на диск
        }

        private void PrinterDiagnostic_Click(object sender, RoutedEventArgs e)
        {
            PrinterDiagnosticText.Text = "Запуск диагностики...\n";
            var prefs = UserPreferences.Instance;
            string? devicePath = prefs.ReceiptDevicePath?.Trim();

            if (string.IsNullOrEmpty(devicePath))
            {
                PrinterDiagnosticText.Text += "❌ Не указан порт принтера.\n" +
                    "Перейдите на вкладку «Печать» и укажите LPT-порт или COM-порт.";
                return;
            }

            // Определяем тип порта
            bool isCom = devicePath.StartsWith("COM", StringComparison.OrdinalIgnoreCase);
            bool isLpt = devicePath.StartsWith("LPT", StringComparison.OrdinalIgnoreCase);
            bool isUsb = devicePath.StartsWith("USB", StringComparison.OrdinalIgnoreCase) ||
                         devicePath.Contains("VID_", StringComparison.OrdinalIgnoreCase); // USB-идентификатор

            if (isCom)
            {
                // ========== COM-порт – полная диагностика ==========
                try
                {
                    string portName = devicePath.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries)[0];
                    int baudRate = prefs.ScaleBaudRate;
                    using (var serial = new System.IO.Ports.SerialPort(portName, baudRate))
                    {
                        serial.ReadTimeout = 1000;
                        serial.WriteTimeout = 1000;
                        serial.Open();

                        // 1. Запрос статуса (DLE EOT 1)
                        byte[] request = new byte[] { 0x10, 0x04, 0x01 };
                        serial.Write(request, 0, request.Length);
                        try
                        {
                            int status = serial.ReadByte();
                            PrinterDiagnosticText.Text += $"📟 Принтер ответил. Статус: 0x{status:X2}\n";
                            if ((status & 0x04) != 0) PrinterDiagnosticText.Text += "• Обнаружена ошибка принтера (конец бумаги?).\n";
                            if ((status & 0x08) != 0) PrinterDiagnosticText.Text += "• Принтер в режиме off-line.\n";
                            if ((status & 0x20) != 0) PrinterDiagnosticText.Text += "• Крышка открыта.\n";
                            if ((status & 0x40) != 0) PrinterDiagnosticText.Text += "• Бумага подана.\n";
                        }
                        catch (TimeoutException)
                        {
                            PrinterDiagnosticText.Text += "⚠️ Принтер не ответил на запрос статуса (таймаут).\n";
                        }

                        // 2. Запрос ID (GS I)
                        byte[] gsI = new byte[] { 0x1D, 0x49, 0x01 };
                        serial.Write(gsI, 0, gsI.Length);
                        System.Threading.Thread.Sleep(200);
                        byte[] buffer = new byte[256];
                        int bytesRead = 0;
                        try { bytesRead = serial.Read(buffer, 0, buffer.Length); }
                        catch (TimeoutException) { }

                        if (bytesRead > 0)
                        {
                            string id = System.Text.Encoding.ASCII.GetString(buffer, 0, bytesRead);
                            PrinterDiagnosticText.Text += $"🆔 ID принтера: {id.Trim()}\n";
                            if (id.Contains("RP") || id.Contains("Rongta"))
                            {
                                PrinterDiagnosticText.Text += "💡 Рекомендуется кодировка wpc1251 и таблица ESC t = 46.\n";
                                _recommendedEncoding = "wpc1251";
                                _recommendedEscTable = "46";
                                ApplyRecommendedPrinterSettingsButton.Visibility = Visibility.Visible;
                            }
                            else
                            {
                                PrinterDiagnosticText.Text += "💡 Попробуйте кодировку wpc1251 или cp866; если не поможет — utf-8, koi8-r, iso-8859-5.\n";
                                ApplyRecommendedPrinterSettingsButton.Visibility = Visibility.Collapsed;
                            }
                        }
                        else
                        {
                            PrinterDiagnosticText.Text += "• ID принтера не получен.\n";
                            PrinterDiagnosticText.Text += "💡 Рекомендуется перебрать кодировки: wpc1251, cp866, utf-8, koi8-r, iso-8859-5.\n";
                            PrinterDiagnosticText.Text += "   Для каждой меняйте таблицу ESC t: Авто, 46, 17, 0, 53, 26.\n";
                            ApplyRecommendedPrinterSettingsButton.Visibility = Visibility.Collapsed;
                        }
                    }
                }
                catch (Exception ex)
                {
                    PrinterDiagnosticText.Text += $"❌ Ошибка диагностики: {ex.Message}\n";
                }
            }
            else
            {
                // ========== LPT / USB / файловый порт – подсказка ==========
                string portType = isLpt ? "LPT" : (isUsb ? "USB" : "файловый");
                PrinterDiagnosticText.Text += $"⚠️ Принтер подключен через {portType}. Автоматический опрос невозможен.\n\n";
                PrinterDiagnosticText.Text += "Надёжная схема настройки:\n";
                PrinterDiagnosticText.Text += "1) Нажмите кнопку ниже, чтобы установить кодировку wpc1251.\n";
                PrinterDiagnosticText.Text += "2) Нажмите «Тест печати».\n";
                PrinterDiagnosticText.Text += "3) Если текст нечитаемый, вручную выберите cp866 в поле «Кодировка текста» и снова «Тест печати».\n";
                PrinterDiagnosticText.Text += "4) Если и это не помогло – переберите остальные кодировки (utf-8, koi8-r, iso-8859-5).\n";
                PrinterDiagnosticText.Text += "   Для каждой меняйте таблицу ESC t: Авто, 46, 17, 0, 53, 26.\n";
                PrinterDiagnosticText.Text += "5) Убедитесь, что кабель подключён, а порт правильно указан в Windows.\n";

                // Предлагаем применить wpc1251 как самый частый вариант
                _recommendedEncoding = "wpc1251";
                _recommendedEscTable = "46";
                ApplyRecommendedPrinterSettingsButton.Visibility = Visibility.Visible;
            }
        }

        private void TestCyrillic_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = UserPreferences.Instance.ToReceiptPrinterSettings();
                if (string.IsNullOrWhiteSpace(cfg.DevicePath))
                {
                    MessageBox.Show("Не указан порт принтера.\nЗайдите в настройки и укажите порт (например, LPT1).",
                        "Принтер", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Тестовый текст (как в вашем примере, только без прямой работы с портом)
                string testText = string.Join("\n",
                    new[]
                    {
                new string('=', ReceiptLayout.CharWidth),
                "        NUR MARKET KASSA        ",
                new string('=', ReceiptLayout.CharWidth),
                "Проверка печати кириллицы!",
                "Тест русского языка прошел успешно.",
                "Работает без прошивки через 46!",
                "АБВГДЕЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ",
                "абвгдежзийклмнопрстуфхцчшщъыьэюя",
                new string('=', ReceiptLayout.CharWidth),
                ""
                    });

                // Создаём копию настроек вручную (метода Clone нет)
                var cfgCopy = new ReceiptPrinterSettings
                {
                    DevicePath = cfg.DevicePath,
                    TextEncoding = "windows-1251",   // <--- принудительно кириллица
                    Enabled = cfg.Enabled,
                    EscPosTableByte = cfg.EscPosTableByte,
                    EscRByte = cfg.EscRByte,
                    RetryCount = cfg.RetryCount
                };

                EscPosTextReceiptPrinter.Print(cfgCopy, testText);
                MessageBox.Show("Пробный чек отправлен на принтер.", "Успех",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка печати:\n\n" + ex.Message, "Принтер",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyRecommendedPrinterSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_recommendedEncoding != null)
            {
                SelectComboByTag(ReceiptEncCombo, _recommendedEncoding);
            }
            if (_recommendedEscTable != null)
            {
                foreach (ComboBoxItem item in ReceiptTableCombo.Items)
                {
                    if (item?.Tag?.ToString() == _recommendedEscTable)
                    {
                        ReceiptTableCombo.SelectedItem = item;
                        break;
                    }
                }
            }

            PrinterDiagnosticText.Text += "\n✅ Рекомендованные настройки установлены. Нажмите «Тест печати» для проверки.\n" +
                "Если текст читается правильно, нажмите «Сохранить» внизу окна.";
        }

        private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn) btn.IsEnabled = false;
            UpdateProgressBar.Visibility = Visibility.Visible;
            UpdateProgressBar.Value = 0;
            UpdateStatusText.Visibility = Visibility.Visible;
            UpdateStatusText.Text = "Проверка обновлений…";

            try
            {
                var manifest = await _updateService.CheckAsync();
                if (manifest == null)
                {
                    UpdateStatusText.Text = "Обновлений нет или не удалось проверить.";
                    return;
                }

                if (MessageBox.Show(
                        $"Доступна новая версия: {manifest.LatestVersion}\nСкачать и установить обновление?",
                        "Обновление",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    UpdateStatusText.Text = "Загрузка обновления…";
                    var progress = new Progress<double>(p =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UpdateProgressBar.Value = p;
                            UpdateStatusText.Text = $"Загрузка: {p:F0}%";
                        });
                    });

                    bool success = await _updateService.DownloadAndRunAsync(manifest.DownloadUrl, progress);
                    if (success)
                    {
                        MessageBox.Show("Обновление загружено. Приложение будет перезапущено.", "Успех",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        await Task.Delay(500);
                        Environment.Exit(0);
                    }
                    else
                    {
                        UpdateStatusText.Text = "Ошибка при установке обновления.";
                    }
                }
                else
                {
                    UpdateStatusText.Text = "";
                }
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = "Ошибка: " + ex.Message;
            }
            finally
            {
                if (sender is Button b) b.IsEnabled = true;
                UpdateProgressBar.Visibility = Visibility.Collapsed;
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(5000);
                    UpdateStatusText.Visibility = Visibility.Collapsed;
                });
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

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var prefs = UserPreferences.Instance;

            prefs.ScaleEnabled = ScaleEnabledCheck.IsChecked == true;
            prefs.ScaleComPort = HardwarePortHelper.NormalizeComPort(ScaleComCombo.Text);
            int.TryParse(ScaleBaudBox.Text.Trim(), out int baud);
            prefs.ScaleBaudRate = baud > 0 ? baud : 9600;
            prefs.ScaleRequestHex = string.IsNullOrWhiteSpace(ScaleHexBox.Text) ? null : ScaleHexBox.Text.Trim();
            int.TryParse(ScalePollBox.Text.Trim(), out int poll);
            prefs.ScalePollMs = poll >= 0 ? poll : 0;

            prefs.ReceiptEnabled = ReceiptEnabledCheck.IsChecked == true;
            prefs.ReceiptDevicePath = HardwarePortHelper.NormalizeLptPort(ReceiptLptBox.Text);
            prefs.ReceiptEncoding = (ReceiptEncCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "wpc1251";

            prefs.ReceiptEscPosTable = null;
            if (ReceiptTableCombo.SelectedItem is ComboBoxItem tableItem && int.TryParse(tableItem.Tag?.ToString(), out int tableByte))
                prefs.ReceiptEscPosTable = tableByte;
            if (int.TryParse(ReceiptEscRBox.Text.Trim(), out int escR))
                prefs.ReceiptEscR = escR;
            else
                prefs.ReceiptEscR = null;
            int.TryParse(ReceiptRetryBox.Text.Trim(), out int retry);
            prefs.ReceiptRetryCount = retry >= 1 ? retry : 3;

            try
            {
                if (prefs.ScaleEnabled) ScaleReaderService.ValidateSettings(prefs.ToScaleSettings());
                if (prefs.ReceiptEnabled) EscPosTextReceiptPrinter.ValidateSettings(prefs.ToReceiptPrinterSettings());
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
                ShowAlert("PrinterAlert", "PrinterAlertText", "Не указан порт принтера.", true);
                return;
            }
            if (!cfg.Enabled)
            {
                ShowAlert("PrinterAlert", "PrinterAlertText", "Печать выключена. Включите на вкладке «Печать».", true);
                return;
            }
            try
            {
                EscPosTextReceiptPrinter.ValidateSettings(cfg);
                EscPosSelfCheckPrinter.PrintSelfCheck(cfg);
                ShowAlert("PrinterAlert", "PrinterAlertText", "Тестовая страница отправлена.", false);
            }
            catch (Exception ex)
            {
                ShowAlert("PrinterAlert", "PrinterAlertText", "Ошибка принтера: " + ex.Message, true);
            }
        }

        private void CheckScale_Click(object sender, RoutedEventArgs e)
        {
            var prefs = UserPreferences.Instance;
            if (!prefs.ScaleEnabled)
            {
                ShowAlert("ScaleAlert", "ScaleAlertText", "Весы выключены. Включите на вкладке «Весы».", true);
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
                string msg = weight.HasValue ? $"Текущий вес: {weight.Value:F3} кг. Статус: {status}." : $"Статус: {status}. Данные не получены.";
                ShowAlert("ScaleAlert", "ScaleAlertText", msg, false);
            }
            catch (Exception ex)
            {
                ShowAlert("ScaleAlert", "ScaleAlertText", "Ошибка весов: " + ex.Message, true);
            }
        }

        private void ShowAlert(string borderName, string textBlockName, string message, bool isError)
        {
            var border = (Border)FindName(borderName);
            var textBlock = (TextBlock)FindName(textBlockName);
            if (border == null || textBlock == null) return;
            border.Visibility = Visibility.Visible;
            textBlock.Text = message;
            border.Background = isError ? System.Windows.Media.Brushes.LightYellow : System.Windows.Media.Brushes.LightGreen;
            border.BorderBrush = isError ? System.Windows.Media.Brushes.Orange : System.Windows.Media.Brushes.Green;
        }
    }
}