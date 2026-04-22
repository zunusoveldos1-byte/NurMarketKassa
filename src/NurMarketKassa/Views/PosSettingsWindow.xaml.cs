using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using NurMarketKassa.Services;

namespace NurMarketKassa.Views;

public partial class PosSettingsWindow : Window
{
    public PosSettingsWindow()
    {
        InitializeComponent();
        var p = UserPreferences.Instance;
        ScaleEnabledCheck.IsChecked = p.ScaleEnabled;
        ScaleBaudBox.Text = p.ScaleBaudRate.ToString();
        ScaleHexBox.Text = p.ScaleRequestHex ?? "";
        ScalePollBox.Text = p.ScalePollMs.ToString();

        ReceiptEnabledCheck.IsChecked = p.ReceiptEnabled;
        ReceiptLptBox.Text = p.ReceiptDevicePath;
        ReceiptEscRBox.Text = p.ReceiptEscR?.ToString() ?? "";
        ReceiptRetryBox.Text = p.ReceiptRetryCount.ToString();

        FullscreenCheck.IsChecked = p.Fullscreen;
        AutostartCheck.IsChecked = p.Autostart || AutostartHelper.IsEnabled();
        AutoTouchKeyboardCheck.IsChecked = p.AutoShowTouchKeyboard;

        var ports = ScaleReaderService.GetAvailablePorts().ToList();
        if (!ports.Contains(p.ScaleComPort, StringComparer.OrdinalIgnoreCase))
            ports.Insert(0, p.ScaleComPort);
        ScaleComCombo.ItemsSource = ports;
        ScaleComCombo.Text = p.ScaleComPort;

        SelectComboByTag(ReceiptEncCombo, p.ReceiptEncoding.ToLowerInvariant());
        var tableTag = p.ReceiptEscPosTable?.ToString() ?? "";
        foreach (ComboBoxItem? it in ReceiptTableCombo.Items)
        {
            if (it?.Tag?.ToString() == tableTag)
            {
                ReceiptTableCombo.SelectedItem = it;
                break;
            }
        }

        if (ReceiptTableCombo.SelectedItem == null && ReceiptTableCombo.Items.Count > 0)
            ReceiptTableCombo.SelectedIndex = 0;

        AppVersionText.Text = "Текущая версия: " + AppVersionInfo.CurrentVersionLabel;
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await AppUpdateService.CheckNowAsync(this, App.Settings.Updates).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Обновление", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void SelectComboByTag(ComboBox box, string value)
    {
        foreach (ComboBoxItem? it in box.Items)
        {
            if (string.Equals(it?.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = it;
                return;
            }
        }

        if (box.Items.Count > 0)
            box.SelectedIndex = 0;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var p = UserPreferences.Instance;
        p.ScaleEnabled = ScaleEnabledCheck.IsChecked == true;
        p.ScaleComPort = HardwarePortHelper.NormalizeComPort(ScaleComCombo.Text, "COM2");
        if (!int.TryParse(ScaleBaudBox.Text.Trim(), out var baud) || baud <= 0)
            baud = 9600;
        p.ScaleBaudRate = baud;
        p.ScaleRequestHex = string.IsNullOrWhiteSpace(ScaleHexBox.Text) ? null : ScaleHexBox.Text.Trim();
        if (!int.TryParse(ScalePollBox.Text.Trim(), out var poll) || poll < 0)
            poll = 0;
        p.ScalePollMs = poll;

        p.ReceiptEnabled = ReceiptEnabledCheck.IsChecked == true;
        p.ReceiptDevicePath = HardwarePortHelper.NormalizeLptPort(ReceiptLptBox.Text, "LPT1");
        if (ReceiptEncCombo.SelectedItem is ComboBoxItem encIt && encIt.Tag is string encTag)
            p.ReceiptEncoding = encTag;
        else
            p.ReceiptEncoding = "wpc1251";

        p.ReceiptEscPosTable = null;
        if (ReceiptTableCombo.SelectedItem is ComboBoxItem tIt)
        {
            var tagStr = tIt.Tag?.ToString();
            if (!string.IsNullOrEmpty(tagStr) && int.TryParse(tagStr, out var tb))
                p.ReceiptEscPosTable = tb;
        }

        var escR = ReceiptEscRBox.Text.Trim();
        p.ReceiptEscR = string.IsNullOrEmpty(escR) ? null : int.TryParse(escR, out var er) ? er : null;

        if (!int.TryParse(ReceiptRetryBox.Text.Trim(), out var retry) || retry < 1)
            retry = 3;
        p.ReceiptRetryCount = retry;

        try
        {
            if (p.ScaleEnabled)
                ScaleReaderService.ValidateSettings(p.ToScaleSettings());
            if (p.ReceiptEnabled)
                EscPosTextReceiptPrinter.ValidateSettings(p.ToReceiptPrinterSettings());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Настройки кассы", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        p.Fullscreen = FullscreenCheck.IsChecked == true;
        p.Autostart = AutostartCheck.IsChecked == true;
        p.AutoShowTouchKeyboard = AutoTouchKeyboardCheck.IsChecked == true;

        p.SaveToDisk();
        AutostartHelper.SyncFromPreference(p.Autostart);

        if (Owner is MainWindow mw)
            mw.ApplyHardwareAndUiPreferences();

        DialogResult = true;
    }

    private void ScaleComCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {

    }
}
