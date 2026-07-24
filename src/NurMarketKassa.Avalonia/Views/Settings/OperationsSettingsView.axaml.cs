using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NurMarketKassa.Configuration;
using NurMarketKassa.Models;

namespace NurMarketKassa.AvaloniaHost.Views.Settings;

public partial class OperationsSettingsView : UserControl
{
    private ObservableCollection<BankQrSetting> _bankSettings = new();
    private readonly string[] _banks = { "Элкарт", "MBank", "ФинкаБанк" };
    private readonly Dictionary<string, string> _logoMap = new()
    {
        { "Элкарт", "avares://NurMarketKassa.Assets/Assets/Elkart-logo.png" },
        { "MBank", "avares://NurMarketKassa.Assets/Assets/Mbank-logo.png" },
        { "ФинкаБанк", "avares://NurMarketKassa.Assets/Assets/Finca-logo.png" }
    };

    public OperationsSettingsView()
    {
        InitializeComponent();
    }

    public void LoadBankQrSettings()
    {
        _bankSettings = new ObservableCollection<BankQrSetting>();
        var prefs = UserPreferences.Instance;
        foreach (var bank in _banks)
        {
            string? qrPath = prefs.BankQrPaths?.TryGetValue(bank, out var qr) == true ? qr : null;
            _bankSettings.Add(new BankQrSetting
            {
                BankName = bank,
                LogoPath = _logoMap[bank],
                QrCodePath = qrPath
            });
        }

        BankQrItemsControl.ItemsSource = _bankSettings;
    }

    private async void LoadQrCode_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BankQrSetting setting)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Выберите QR-код для банка {setting.BankName}",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Изображения") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" } }
            }
        });

        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (string.IsNullOrEmpty(path))
            return;

        setting.QrCodePath = path;
        SaveBankQrSettings();
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

        prefs.SaveToDisk();
    }
}
