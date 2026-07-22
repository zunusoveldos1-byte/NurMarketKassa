using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NurMarketKassa.AvaloniaHost.Services;
using NurMarketKassa.Interfaces;
using NurMarketKassa.Services;
using NurMarketKassa.Services.Hardware;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class WeighedProductDialog : Window
{
    private readonly IWeightScaleService? _scale;
    private readonly bool _scaleLive;
    private readonly DispatcherTimer? _timer;

    public string QuantityNormalized { get; private set; } = "";

    public WeighedProductDialog() : this("", "", null) { }

    public WeighedProductDialog(
        string productTitle,
        string pricePerKgLine,
        IWeightScaleService? scale,
        string? initialKg = null,
        string okButtonText = "В чек",
        string? windowTitle = null)
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
        ScalePanel.IsVisible = scaleConfigured;
        FromScaleButton.IsVisible = _scaleLive;

        WeightBox.Text = !string.IsNullOrEmpty(initialKg) ? initialKg : "0.00";
        LiveScaleText.Text = FormatLiveScaleText();

        if (scaleConfigured)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += (_, _) => LiveScaleText.Text = FormatLiveScaleText();
            Opened += (_, _) => _timer.Start();
            Closed += (_, _) => _timer.Stop();
        }

        Opened += (_, _) =>
        {
            WeightBox.Focus();
            WeightBox.SelectAll();
        };
    }

    private static bool HasLiveScaleConnection(IWeightScaleService? scale)
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
        return _scale?.LastWeight is double w
            ? w.ToString("0.00", CultureInfo.InvariantCulture) + " кг"
            : "0.00 кг";
    }

    private void FromScale_Click(object? sender, RoutedEventArgs e)
    {
        if (_scale == null || !_scaleLive)
        {
            PosMessageBox.Show(this, "Весы не подключены — укажите вес вручную.", "Весы",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_scale.LastWeight is not double w || w <= 0)
        {
            PosMessageBox.Show(this, "Нет веса с весов.", "Весы",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        WeightBox.Text = w.ToString("0.###", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        WeightBox.SelectAll();
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => TryCloseOk();

    private void WeightBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TryCloseOk();
        }
    }

    private void TryCloseOk()
    {
        var raw = (WeightBox.Text ?? "").Trim().Replace(',', '.');
        if (raw.Length == 0)
        {
            PosMessageBox.Show(this, "Введите вес.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var kg) || kg <= 0)
        {
            PosMessageBox.Show(this, "Вес должен быть положительным числом.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        QuantityNormalized = JsonNumericReader.FormatWeightForApi((double)kg) ?? "0";
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
