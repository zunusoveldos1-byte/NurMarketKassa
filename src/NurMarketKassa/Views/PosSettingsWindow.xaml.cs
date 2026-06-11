using NurMarketKassa.Configuration;
using NurMarketKassa.Models;
using NurMarketKassa.Services;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
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

        //private string? _recommendedEncoding;
        //private string? _recommendedEscTable;   

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
            // Загрузка названия магазина
            StoreNameBox.Text = prefs.StoreName;
            // Загрузка элементов чека
            ShowStoreNameCheck.IsChecked = prefs.ShowStoreName;
            ShowAddressCheck.IsChecked = prefs.ShowAddress;
            ShowReceiptNumberCheck.IsChecked = prefs.ShowReceiptNumber;
            ShowDateCheck.IsChecked = prefs.ShowDate;
            ShowItemsCheck.IsChecked = prefs.ShowItems;
            ShowTotalCheck.IsChecked = prefs.ShowTotal;
            ShowQrCodeCheck.IsChecked = prefs.ShowQrCode;

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

            // === Загрузка настроек графического чека ===
            GraphicReceiptEnabledCheck.IsChecked = prefs.GraphicReceiptEnabled;
            TextModeRadio.IsChecked = prefs.SelectedPrintMode == PrintMode.Text;
            GraphicModeRadio.IsChecked = prefs.SelectedPrintMode == PrintMode.Graphic;

            GraphicWidthBox.Text = prefs.GraphicPaperWidthPixels.ToString();
            SelectComboByTag(GraphicFontCombo, prefs.GraphicFontFamily);

            string savedFont = prefs.GraphicFontFamily;
            foreach (ComboBoxItem item in GraphicFontCombo.Items)
            {
                if (item.Tag?.ToString() == savedFont)
                {
                    GraphicFontCombo.SelectedItem = item;
                    break;
                }
            }
            if (GraphicFontCombo.SelectedItem == null)
                GraphicFontCombo.SelectedIndex = 0;

            // Загрузка размера шрифта
            var fontSize = prefs.GraphicFontSize;
            foreach (ComboBoxItem item in GraphicFontSizeCombo.Items)
            {
                if (item.Tag != null && float.TryParse(item.Tag.ToString(), out float val) && Math.Abs(val - fontSize) < 0.01)
                {
                    GraphicFontSizeCombo.SelectedItem = item;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(prefs.QrCodePath))
                GraphicQrStatusText.Text = $"✅ QR-код сохранён: {System.IO.Path.GetFileName(prefs.QrCodePath)}";
            else
                GraphicQrStatusText.Text = "QR-код не загружен";

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

            // Сохранение элементов чека
            prefs.ShowStoreName = ShowStoreNameCheck.IsChecked == true;
            prefs.ShowAddress = ShowAddressCheck.IsChecked == true;
            prefs.ShowReceiptNumber = ShowReceiptNumberCheck.IsChecked == true;
            prefs.ShowDate = ShowDateCheck.IsChecked == true;
            prefs.ShowItems = ShowItemsCheck.IsChecked == true;
            prefs.ShowTotal = ShowTotalCheck.IsChecked == true;
            prefs.ShowQrCode = ShowQrCodeCheck.IsChecked == true;

            // Сохранение названия магазина
            prefs.StoreName = StoreNameBox.Text.Trim();
            if (string.IsNullOrEmpty(prefs.StoreName))
                prefs.StoreName = "MARKET PLUS";

            if (Owner is MainWindow mainWindow)
                mainWindow.ApplyHardwareAndUiPreferences();

            // === Сохранение графических настроек ===
            prefs.GraphicReceiptEnabled = GraphicReceiptEnabledCheck.IsChecked == true;

            // Сохранение размера шрифта
            if (GraphicFontSizeCombo.SelectedItem is ComboBoxItem sizeItem && sizeItem.Tag != null)
            {
                if (float.TryParse(sizeItem.Tag.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out float size))
                    prefs.GraphicFontSize = size;
            }

            // Сохранение выбранного режима печати
            if (TextModeRadio.IsChecked == true)
                prefs.SelectedPrintMode = PrintMode.Text;
            else if (GraphicModeRadio.IsChecked == true)
                prefs.SelectedPrintMode = PrintMode.Graphic;

            // Ширина бумаги
            if (int.TryParse(GraphicWidthBox.Text.Trim(), out int width) && width >= 200)
                prefs.GraphicPaperWidthPixels = width;
            else
                prefs.GraphicPaperWidthPixels = 384; // значение по умолчанию

            // Шрифт
            var fontItem = GraphicFontCombo.SelectedItem as ComboBoxItem;
            prefs.GraphicFontFamily = fontItem?.Tag?.ToString() ?? "Consolas";
            // QrCodePath сохраняется отдельно в LoadGraphicQrCode_Click

            DialogResult = true;
        }

        // --- Обработчики графического чека ---

        private void LoadGraphicQrCode_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp",
                Title = "Выберите QR-код (сохранится для будущего)"
            };
            if (dlg.ShowDialog() == true)
            {
                var prefs = UserPreferences.Instance;
                prefs.QrCodePath = dlg.FileName;
                prefs.SaveToDisk();
                GraphicQrStatusText.Text = $"✅ QR-код сохранён: {System.IO.Path.GetFileName(dlg.FileName)}";
            }
        }

        private void DeleteGraphicQrCode_Click(object sender, RoutedEventArgs e)
        {
            var prefs = UserPreferences.Instance;
            prefs.QrCodePath = "";
            prefs.SaveToDisk();
            GraphicQrStatusText.Text = "QR-код не загружен";
        }

        private void TestGraphicPrint_Click(object sender, RoutedEventArgs e)
        {
            var prefs = UserPreferences.Instance;

            // 1. Проверяем, включён ли графический чек вообще
            if (!prefs.GraphicReceiptEnabled)
            {
                StatusText.Text = "❌ Графический чек выключен. Включите его в настройках (чекбокс «Включить графический чек»).";
                return;
            }

            // 2. Проверяем, выбран ли графический режим
            if (prefs.SelectedPrintMode != PrintMode.Graphic)
            {
                StatusText.Text = "❌ Сейчас выбран текстовый режим. Переключите на графический в настройках.";
                return;
            }

            // 3. Проверяем порт принтера
            string devicePath = prefs.ReceiptDevicePath?.Trim() ?? "LPT1";
            if (string.IsNullOrEmpty(devicePath))
            {
                StatusText.Text = "❌ Укажите порт принтера!";
                return;
            }

            StatusText.Text = "🔄 Печать графического чека...";

            // 4. Формируем тестовый текст
            string testText = string.Join("\n",
                new[]
                {
            "==================================",
            "      NUR MARKET KASSA          ",
            "==================================",
            "Привет мир!",
            "Тест кириллицы: АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ",
            "Кыргызча текст: Салам дүйнө!",
            "==================================",
            "ИТОГО: 1 234,56 сом",
            "СПАСИБО ЗА ПОКУПКУ!",
            "==================================",
            ""
                });

            try
            {
                // 5. Создаём настройки для графического чека (берём всё из преференсов)
                var settings = new GraphicReceiptSettings
                {
                    PaperWidthPixels = prefs.GraphicPaperWidthPixels,
                    FontFamily = prefs.GraphicFontFamily,
                    FontSize = prefs.GraphicFontSize, // <- НОВОЕ!
                    DevicePath = devicePath,
                    ShowStoreName = prefs.ShowStoreName,
                    ShowAddress = prefs.ShowAddress,
                    ShowReceiptNumber = prefs.ShowReceiptNumber,
                    ShowDate = prefs.ShowDate,
                    ShowItems = prefs.ShowItems,
                    ShowTotal = prefs.ShowTotal,
                    ShowQrCode = false,
                    QrCodePath = prefs.QrCodePath
                };

                // 6. Генерируем изображение
                byte[] receiptBytes = GraphicReceiptGenerator.GenerateReceiptImage(testText, settings);

                // 7. Отправка на принтер (прямая запись в порт)
                File.WriteAllBytes(devicePath, receiptBytes);

                // 8. Успех
                StatusText.Text = $"✅ Графический чек отправлен на {devicePath}\n(Размер: {receiptBytes.Length} байт)";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Ошибка: {ex.Message}";
            }
        }

        private void TestTextPrint_Click(object sender, RoutedEventArgs e)
        {
            var cfg = UserPreferences.Instance.ToReceiptPrinterSettings();
            if (string.IsNullOrWhiteSpace(cfg.DevicePath))
            {
                StatusText.Text = "❌ Не указан порт принтера!";
                return;
            }

            string testText = string.Join("\n", new[]
            {
        "==================================",
        "      NUR MARKET KASSA          ",
        "==================================",
        "Тест текстовой печати (ESC/POS)",
        "Кириллица: АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯ",
        "Кыргызча: Салам дүйнө!",
        "==================================",
        "Дата: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"),
        "==================================",
        "Если этот текст читается —",
        "кодировка и таблица подобраны верно.",
        "==================================",
        ""
    });

            try
            {
                // Принудительно используем настройки текстовой печати (без графики)
                EscPosTextReceiptPrinter.Print(cfg, testText);
                StatusText.Text = $"✅ Текстовый чек отправлен на {cfg.DevicePath}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Ошибка текстовой печати: {ex.Message}";
            }
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