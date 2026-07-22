using NurMarketKassa.Configuration;
using NurMarketKassa.Models;
using NurMarketKassa.Services;
using NurMarketKassa.AvaloniaHost.Views.Dialogs; using NurMarketKassa.AvaloniaHost.Services;
using NurMarketKassa.ViewModels.Settings;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia; using Avalonia.Controls; using Avalonia.Interactivity; using Avalonia.Media; using Avalonia.Threading; using Avalonia.Platform.Storage;



#nullable enable

namespace NurMarketKassa.AvaloniaHost.Views
{
    public partial class PosSettingsWindow : Window
    {
        public SettingsViewModel SettingsVm { get; }

        //private string? _recommendedEncoding;
        //private string? _recommendedEscTable;   

        private ObservableCollection<BankQrSetting> _bankSettings;
        private readonly string[] _banks = { "Элкарт", "MBank", "ФинкаБанк" };
        private readonly Dictionary<string, string> _logoMap = new()
        {
    { "Элкарт",   "avares://NurMarketKassa.Assets/Assets/Elkart-logo.png" },
    { "MBank",    "avares://NurMarketKassa.Assets/Assets/Mbank-logo.png" },
    { "ФинкаБанк", "avares://NurMarketKassa.Assets/Assets/Finca-logo.png" }
        };
        public PosSettingsWindow() : this(ResolveSettingsViewModel())
        {
        }

        private static SettingsViewModel ResolveSettingsViewModel() =>
            NurMarketKassa.AvaloniaHost.App.GetRequiredService<SettingsViewModel>();

        public PosSettingsWindow(SettingsViewModel settingsVm)
        {
            SettingsVm = settingsVm;
            DataContext = this;
            InitializeComponent();

            _bankSettings = new ObservableCollection<BankQrSetting>();  // ← инициализация
            LoadBankQrSettings();  // теперь безопасно

            if (UserPreferences.Instance.Fullscreen)
            {
                SystemDecorations = SystemDecorations.None;
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

            SelectComboByTag(ReceiptPaperWidthCombo, prefs.ReceiptPaperWidthMm.ToString(CultureInfo.InvariantCulture));
            ApplyPaperWidthToUi(prefs.ReceiptPaperWidthMm);

            FullscreenCheck.IsChecked = prefs.Fullscreen;
            AutostartCheck.IsChecked = prefs.Autostart || AutostartHelper.IsEnabled();
            AutoTouchKeyboardCheck.IsChecked = prefs.AutoShowTouchKeyboard;
            // Загрузка названия магазина
            StoreNameBox.Text = prefs.StoreName;
            StoreAddressBox.Text = prefs.StoreAddress;
            ShowInnCheck.IsChecked = prefs.ShowInn;
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
            ResetManualAddQtyCheck.IsChecked = prefs.ResetManualAddQtyAfterAdd;

            var ports = ScaleReaderService.GetAvailablePorts().ToList();
            if (!ports.Contains(prefs.ScaleComPort, StringComparer.OrdinalIgnoreCase))
                ports.Insert(0, prefs.ScaleComPort);
            ScaleComCombo.ItemsSource = ports;
            SelectScaleComPort(prefs.ScaleComPort);
            RefreshScalePortStatus();

            SelectComboByTag(ReceiptEncCombo, prefs.ReceiptEncoding.ToLowerInvariant());
            string tableTag = prefs.ReceiptEscPosTable?.ToString() ?? "";
            foreach (var item in ReceiptTableCombo.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag?.ToString() == tableTag)
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

            SelectComboByTag(GraphicFontCombo, TestReceiptLineBuilder.FontFamily);

            string savedFont = prefs.GraphicFontFamily;
            foreach (var item in GraphicFontCombo.Items.OfType<ComboBoxItem>())
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
            var fontSize = prefs.GraphicFontSize > 0 ? prefs.GraphicFontSize : TestReceiptLineBuilder.DefaultFontSizePt;
            if (prefs.GraphicFontSize <= 0)
                prefs.GraphicFontSize = TestReceiptLineBuilder.DefaultFontSizePt;

            SelectGraphicFontSizeCombo(fontSize);

            if (!string.IsNullOrEmpty(prefs.QrCodePath))
                GraphicQrStatusText.Text = $"✅ QR-код сохранён: {System.IO.Path.GetFileName(prefs.QrCodePath)}";
            else
                GraphicQrStatusText.Text = "QR-код не загружен";

            AppVersionText.Text = "Текущая версия: " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "неизвестно");
            RefreshCatalogDiagnostics();

            RefreshPrinterPortStatus();
        }

        private void ReceiptLptBox_TextChanged(object sender, TextChangedEventArgs e) =>
            RefreshPrinterPortStatus();

        private void ReceiptPaperWidthCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ReceiptPaperWidthCombo?.SelectedItem is ComboBoxItem item
                && int.TryParse(item.Tag?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int mm))
            {
                ApplyPaperWidthToUi(mm);
            }
        }

        private void ApplyPaperWidthToUi(int paperWidthMm)
        {
            var normalized = ReceiptPaperProfile.NormalizePaperWidthMm(paperWidthMm);
            if (GraphicWidthBox != null)
                GraphicWidthBox.Text = ReceiptPaperProfile.GetRasterWidthPixels(normalized).ToString(CultureInfo.InvariantCulture);
        }

        private static int ReadPaperWidthMmFromUi(ComboBox combo)
        {
            if (combo.SelectedItem is ComboBoxItem item
                && int.TryParse(item.Tag?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int mm))
            {
                return ReceiptPaperProfile.NormalizePaperWidthMm(mm);
            }

            return ReceiptPaperProfile.Paper58mm;
        }

        private void RefreshPrinterPortStatus()
        {
            if (StatusPortText == null)
                return;

            var probe = PrinterPortService.ProbePort(ReceiptLptBox.Text);
            StatusPortText.Text = probe.Message;
            StatusPortText.Foreground = probe.IsAvailable
                ? new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A))
                : new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
        }

        private void ClearPrintError()
        {
            if (TxtPrintErrorDetails != null)
                TxtPrintErrorDetails.Text = "";
            if (PrintErrorPanel != null)
                PrintErrorPanel.IsVisible = false;
        }

        private void ShowPrintError(Exception ex, string devicePath)
        {
            if (TxtPrintErrorDetails == null || PrintErrorPanel == null)
                return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Порт: {devicePath}");

            var current = ex;
            int level = 0;
            while (current != null)
            {
                var prefix = level == 0 ? "Ошибка: " : new string(' ', level * 2) + "↳ ";
                sb.AppendLine($"{prefix}{current.GetType().Name}: {current.Message}");

                if (current is System.ComponentModel.Win32Exception w32)
                    sb.AppendLine($"  Win32-код: {w32.NativeErrorCode}");

                current = current.InnerException;
                level++;
            }

            TxtPrintErrorDetails.Text = sb.ToString().TrimEnd();
            PrintErrorPanel.IsVisible = true;
        }

        private void BtnPhysicalPrint_Click(object sender, RoutedEventArgs e)
        {
            var devicePath = HardwarePortHelper.NormalizeLptPort(ReceiptLptBox.Text);
            if (string.IsNullOrWhiteSpace(devicePath))
            {
                StatusText.Text = "❌ Укажите порт принтера (LPT1, COM3 или имя очереди Windows).";
                return;
            }

            var probe = PrinterPortService.ProbePort(devicePath);
            if (!probe.IsAvailable)
            {
                StatusText.Text = $"❌ Порт недоступен: {probe.Message}";
                RefreshPrinterPortStatus();
                return;
            }

            ClearPrintError();

            try
            {
                var cfg = BuildReceiptSettingsFromUi();
                var contentSettings = BuildGraphicSettingsFromUi(devicePath);
                var storeName = StoreNameBox.Text ?? string.Empty;
                int retry = cfg.RetryCount;

                if (GraphicModeRadio.IsChecked == true)
                {
                    if (GraphicReceiptEnabledCheck.IsChecked != true)
                    {
                        StatusText.Text = "❌ Графический чек выключен. Включите «Включить графический чек».";
                        return;
                    }

                    var settings = BuildGraphicSettingsFromUi(devicePath);
                    var bytes = GraphicReceiptGenerator.GenerateTestReceiptImage(settings, storeName);
                    ReceiptPrintService.SendRawBytes(devicePath, bytes, retry);
                    StatusText.Text = $"✅ Графический чек ({bytes.Length} байт) отправлен на {devicePath}";
                }
                else
                {
                    var testText = ReceiptPdfPreviewService.BuildTextTestReceipt(contentSettings, storeName);
                    var charWidth = ReceiptPaperProfile.GetCharWidth(ReadPaperWidthMmFromUi(ReceiptPaperWidthCombo));
                    var payload = EscPosTextReceiptPrinter.BuildEscPosPayload(cfg, testText, charWidth);
                    ReceiptPrintService.SendRawBytes(devicePath, payload, retry);
                    StatusText.Text = $"✅ Текстовый ESC/POS чек ({payload.Length} байт) отправлен на {devicePath}";
                }

                RefreshPrinterPortStatus();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Ошибка печати в порт: {ex.Message}";
                ShowPrintError(ex, devicePath);
                PosLogger.Log($"Физическая печать: {ex}", "PRINTER");
                RefreshPrinterPortStatus();
            }
        }

        private void ClickToCartMode_Changed(object sender, RoutedEventArgs e)
        {
            var prefs = UserPreferences.Instance;
            prefs.SingleClickToCart = SingleClickToCartRadio.IsChecked == true;
            prefs.ResetManualAddQtyAfterAdd = ResetManualAddQtyCheck.IsChecked == true;
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
        private async void LoadQrCode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BankQrSetting setting)
            {
                var path = await PickImagePathAsync($"Выберите QR-код для банка {setting.BankName}");
                if (!string.IsNullOrEmpty(path))
                {
                    setting.QrCodePath = path;
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
            UpdateProgressBar.IsVisible = true;
            UpdateStatusText.IsVisible = true;
            UpdateStatusText.Text = "Проверка обновлений недоступна в Avalonia-сборке.";
            UpdateProgressBar.IsVisible = false;
            if (sender is Button b) b.IsEnabled = true;
            await Task.CompletedTask;
        }

        private static void SelectComboByTag(ComboBox box, string value)
        {
            foreach (var item in box.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedItem = item;
                    return;
                }
            }
            if (box.Items.Count > 0) box.SelectedIndex = 0;
        }

        private void SelectGraphicFontSizeCombo(float fontSize)
        {
            foreach (var item in GraphicFontSizeCombo.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag != null
                    && float.TryParse(item.Tag.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out float val)
                    && Math.Abs(val - fontSize) < 0.01f)
                {
                    GraphicFontSizeCombo.SelectedItem = item;
                    return;
                }
            }

            GraphicFontSizeCombo.SelectedIndex = 2;
        }

        private float ReadGraphicFontSizeFromUi()
        {
            if (GraphicFontSizeCombo.SelectedItem is ComboBoxItem sizeItem
                && sizeItem.Tag != null
                && float.TryParse(sizeItem.Tag.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out float size)
                && size > 0)
            {
                return size;
            }

            return TestReceiptLineBuilder.DefaultFontSizePt;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close(false);

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var prefs = UserPreferences.Instance;

            prefs.ScaleEnabled = ScaleEnabledCheck.IsChecked == true;
            prefs.ScaleComPort = GetSelectedScaleComPort();
            int.TryParse(ScaleBaudBox.Text?.Trim(), out int baud);
            prefs.ScaleBaudRate = baud > 0 ? baud : 9600;
            prefs.ScaleRequestHex = string.IsNullOrWhiteSpace(ScaleHexBox.Text) ? null : ScaleHexBox.Text.Trim();
            int.TryParse(ScalePollBox.Text?.Trim(), out int poll);
            prefs.ScalePollMs = poll >= 0 ? poll : 0;

            prefs.ReceiptEnabled = ReceiptEnabledCheck.IsChecked == true;
            prefs.ReceiptDevicePath = HardwarePortHelper.NormalizeLptPort(ReceiptLptBox.Text);
            prefs.ReceiptPaperWidthMm = ReadPaperWidthMmFromUi(ReceiptPaperWidthCombo);
            prefs.GraphicPaperWidthPixels = ReceiptPaperProfile.GetRasterWidthPixels(prefs.ReceiptPaperWidthMm);
            prefs.ReceiptEncoding = (ReceiptEncCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "wpc1251";

            prefs.ReceiptEscPosTable = null;
            if (ReceiptTableCombo.SelectedItem is ComboBoxItem tableItem && int.TryParse(tableItem.Tag?.ToString(), out int tableByte))
                prefs.ReceiptEscPosTable = tableByte;
            if (int.TryParse(ReceiptEscRBox.Text?.Trim(), out int escR))
                prefs.ReceiptEscR = escR;
            else
                prefs.ReceiptEscR = null;
            int.TryParse(ReceiptRetryBox.Text?.Trim(), out int retry);
            prefs.ReceiptRetryCount = retry >= 1 ? retry : 3;

            try
            {
                if (prefs.ScaleEnabled) ScaleReaderService.ValidateSettings(prefs.ToScaleSettings());
                if (prefs.ReceiptEnabled) EscPosTextReceiptPrinter.ValidateSettings(prefs.ToReceiptPrinterSettings());
            }
            catch (Exception ex)
            {
                PosMessageBox.Show(ex.Message, "Настройки кассы", MessageBoxButton.OK, MessageBoxImage.Exclamation);
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
            prefs.StoreName = StoreNameBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(prefs.StoreName))
                prefs.StoreName = "MARKET PLUS";
            prefs.StoreAddress = StoreAddressBox.Text?.Trim() ?? string.Empty;
            prefs.ShowInn = ShowInnCheck.IsChecked == true;

            // hardware prefs applied on next restart

            // === Сохранение графических настроек ===
            prefs.GraphicReceiptEnabled = GraphicReceiptEnabledCheck.IsChecked == true;

            // Сохранение размера шрифта
            prefs.GraphicFontSize = ReadGraphicFontSizeFromUi();

            // Сохранение выбранного режима печати
            if (TextModeRadio.IsChecked == true)
                prefs.SelectedPrintMode = PrintMode.Text;
            else if (GraphicModeRadio.IsChecked == true)
                prefs.SelectedPrintMode = PrintMode.Graphic;

            // Ширина бумаги синхронизируется с комбобоксом «Ширина ленты»
            prefs.GraphicPaperWidthPixels = ReceiptPaperProfile.GetRasterWidthPixels(prefs.ReceiptPaperWidthMm);

            // Шрифт
            var fontItem = GraphicFontCombo.SelectedItem as ComboBoxItem;
            prefs.GraphicFontFamily = fontItem?.Tag?.ToString() ?? "Consolas";
            // QrCodePath сохраняется отдельно в LoadGraphicQrCode_Click

            Close(true);
        }

        // --- Обработчики графического чека ---

        private async void LoadGraphicQrCode_Click(object sender, RoutedEventArgs e)
        {
            var path = await PickImagePathAsync("Выберите QR-код (сохранится для будущего)");
            if (!string.IsNullOrEmpty(path))
            {
                var prefs = UserPreferences.Instance;
                prefs.QrCodePath = path;
                prefs.SaveToDisk();
                GraphicQrStatusText.Text = $"✅ QR-код сохранён: {Path.GetFileName(path)}";
            }
        }

        private void DeleteGraphicQrCode_Click(object sender, RoutedEventArgs e)
        {
            var prefs = UserPreferences.Instance;
            prefs.QrCodePath = "";
            prefs.SaveToDisk();
            GraphicQrStatusText.Text = "QR-код не загружен";
        }

        private async void TestGraphicPrint_Click(object sender, RoutedEventArgs e)
        {
            if (!GraphicReceiptEnabledCheck.IsChecked == true)
            {
                StatusText.Text = "❌ Графический чек выключен. Включите его в настройках (чекбокс «Включить графический чек»).";
                return;
            }

            if (GraphicModeRadio.IsChecked != true)
            {
                StatusText.Text = "❌ Сейчас выбран текстовый режим. Переключите на графический в настройках.";
                return;
            }

            try
            {
                var devicePath = HardwarePortHelper.NormalizeLptPort(ReceiptLptBox.Text);
                var settings = BuildGraphicSettingsFromUi(devicePath);
                var storeName = StoreNameBox.Text ?? string.Empty;
                var tempPdfPath = Path.Combine(Path.GetTempPath(), $"test_graphic_{Guid.NewGuid():N}.pdf");

                ReceiptPdfPreviewService.GenerateGraphicReceiptPdf(tempPdfPath, settings, storeName);

                var dialog = new ReceiptPreviewDialog("Предпросмотр: Графический чек", tempPdfPath);
                await dialog.ShowDialog<bool>(this);
                StatusText.Text = "Предпросмотр графического чека готов. Для физической печати нажмите «Печать в сам порт».";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Ошибка: {ex.Message}";
            }
        }

        private async void TestTextPrint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cfg = BuildReceiptSettingsFromUi();
                var contentSettings = BuildGraphicSettingsFromUi(cfg.DevicePath);
                var storeName = StoreNameBox.Text ?? string.Empty;
                var encoding = (ReceiptEncCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "wpc1251";
                int? escTable = null;
                if (ReceiptTableCombo.SelectedItem is ComboBoxItem tableItem
                    && int.TryParse(tableItem.Tag?.ToString(), out int tableByte))
                {
                    escTable = tableByte;
                }

                var testText = ReceiptPdfPreviewService.BuildTextTestReceipt(contentSettings, storeName);
                var tempPdfPath = Path.Combine(Path.GetTempPath(), $"test_pos_{Guid.NewGuid():N}.pdf");

                ReceiptPdfPreviewService.GenerateTextReceiptPdf(tempPdfPath, testText, encoding, escTable);

                var dialog = new ReceiptPreviewDialog("Предпросмотр: Текстовый чек (ESC/POS)", tempPdfPath);
                await dialog.ShowDialog<bool>(this);
                StatusText.Text = "Предпросмотр текстового чека готов. Для физической печати нажмите «Печать в сам порт».";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Ошибка текстовой печати: {ex.Message}";
            }
        }

        private ReceiptPrinterSettings BuildReceiptSettingsFromUi()
        {
            int? tableByte = null;
            if (ReceiptTableCombo.SelectedItem is ComboBoxItem tableItem
                && int.TryParse(tableItem.Tag?.ToString(), out int parsedTable))
            {
                tableByte = parsedTable;
            }

            int? escR = int.TryParse(ReceiptEscRBox.Text?.Trim(), out int parsedEscR) ? parsedEscR : null;
            int.TryParse(ReceiptRetryBox.Text?.Trim(), out int retry);

            return new ReceiptPrinterSettings
            {
                Enabled = ReceiptEnabledCheck.IsChecked == true,
                DevicePath = HardwarePortHelper.NormalizeLptPort(ReceiptLptBox.Text),
                TextEncoding = (ReceiptEncCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "wpc1251",
                EscPosTableByte = tableByte,
                EscRByte = escR,
                RetryCount = retry >= 1 ? retry : 3,
            };
        }

        private GraphicReceiptSettings BuildGraphicSettingsFromUi(string devicePath)
        {
            var prefs = UserPreferences.Instance;
            var paperMm = ReadPaperWidthMmFromUi(ReceiptPaperWidthCombo);
            var paperWidth = ReceiptPaperProfile.GetRasterWidthPixels(paperMm);

            return new GraphicReceiptSettings
            {
                PaperWidthPixels = paperWidth,
                FontFamily = (GraphicFontCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                             ?? TestReceiptLineBuilder.FontFamily,
                FontSize = TestReceiptLineBuilder.ResolveFontSize(ReadGraphicFontSizeFromUi()),
                DevicePath = devicePath,
                ShowStoreName = ShowStoreNameCheck.IsChecked == true,
                ShowAddress = ShowAddressCheck.IsChecked == true,
                ShowInn = ShowInnCheck.IsChecked == true,
                ShowReceiptNumber = ShowReceiptNumberCheck.IsChecked == true,
                ShowDate = ShowDateCheck.IsChecked == true,
                ShowItems = ShowItemsCheck.IsChecked == true,
                ShowTotal = ShowTotalCheck.IsChecked == true,
                ShowQrCode = ShowQrCodeCheck.IsChecked == true,
                QrCodePath = prefs.QrCodePath,
                StoreAddress = StoreAddressBox.Text?.Trim() ?? string.Empty,
                StoreInn = UserPreferences.Instance.StoreInn ?? string.Empty,
                GraphicPrintMode = GraphicModeRadio.IsChecked == true,
            };
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

        private void ScaleComCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
            RefreshScalePortStatus();

        private void SelectScaleComPort(string? savedPort)
        {
            var port = HardwarePortHelper.NormalizeComPort(savedPort, "");
            if (string.IsNullOrWhiteSpace(port))
                return;

            foreach (var item in ScaleComCombo.Items)
            {
                if (item is string existing
                    && string.Equals(existing, port, StringComparison.OrdinalIgnoreCase))
                {
                    ScaleComCombo.SelectedItem = existing;
                    return;
                }
            }

            ScaleComCombo.Items.Add(port);
            ScaleComCombo.SelectedItem = port;
        }

        private string GetSelectedScaleComPort()
        {
            if (ScaleComCombo.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected))
                return HardwarePortHelper.NormalizeComPort(selected);

            return HardwarePortHelper.NormalizeComPort("");
        }

        private void RefreshScalePortStatus()
        {
            if (StatusScalePortText == null)
                return;

            var port = GetSelectedScaleComPort();
            var probe = ScaleReaderService.ProbePort(port);
            StatusScalePortText.Text = probe.Message;
            StatusScalePortText.Foreground = probe.State switch
            {
                ScaleReaderService.ScalePortState.Available => new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A)),
                ScaleReaderService.ScalePortState.Busy => new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C)),
                ScaleReaderService.ScalePortState.NotSpecified => new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                _ => new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
            };
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
            finally
            {
                RefreshScalePortStatus();
            }
        }

        private void RefreshCatalogDiagnostics()
        {
            if (CatalogDiagnosticsText == null)
                return;

            CatalogDiagnosticsText.Text = "Диагностика каталога недоступна в Avalonia-сборке.";
        }

        private void ShowAlert(string borderName, string textBlockName, string message, bool isError)
        {
            var border = this.FindControl<Border>(borderName);
            var textBlock = this.FindControl<TextBlock>(textBlockName);
            if (border == null || textBlock == null) return;
            border.IsVisible = true;
            textBlock.Text = message;
            border.Background = isError ? Brushes.LightYellow : Brushes.LightGreen;
            border.BorderBrush = isError ? Brushes.Orange : Brushes.Green;
        }

        private async Task<string?> PickImagePathAsync(string title)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Изображения") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" } }
                }
            });
            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }
    }
}
