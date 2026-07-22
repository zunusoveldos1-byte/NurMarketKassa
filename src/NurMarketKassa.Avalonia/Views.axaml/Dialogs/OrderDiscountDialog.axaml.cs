using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class OrderDiscountDialog : Window
{
    public string DiscountMode { get; private set; } = "percent";
    public string DiscountScope { get; private set; } = "check";
    public string DiscountValue { get; private set; } = "";
    public bool ClearRequested { get; private set; }

    public OrderDiscountDialog() : this("", "") { }

    public OrderDiscountDialog(string currentPercent, string currentSum)
    {
        InitializeComponent();

        var pct = (currentPercent ?? "").Trim();
        var sum = (currentSum ?? "").Trim();

        if (!string.IsNullOrEmpty(sum))
        {
            RbSum.IsChecked = true;
            ValueBox.Text = sum;
        }
        else
        {
            RbPercent.IsChecked = true;
            ValueBox.Text = pct;
        }

        SyncModeUi();
    }

    public void SetItemMode(string itemTitle, string? currentDiscountType, decimal? currentDiscountValue)
    {
        Title = "Скидка на товар";
        HeaderTitleText.Text = "Скидка на товар";
        ItemTitleLabel.Text = itemTitle;
        ItemTitleLabel.IsVisible = true;
        ScopePanel.IsVisible = false;
        RbItem.IsChecked = true;
        DiscountScope = "item";

        if (currentDiscountValue.HasValue && currentDiscountType != null)
        {
            if (currentDiscountType == "percent")
            {
                RbPercent.IsChecked = true;
                ValueBox.Text = currentDiscountValue.Value.ToString("F0", CultureInfo.InvariantCulture);
            }
            else if (currentDiscountType == "sum")
            {
                RbSum.IsChecked = true;
                ValueBox.Text = currentDiscountValue.Value.ToString("F2", CultureInfo.InvariantCulture);
            }
        }
        else
        {
            ValueBox.Text = "";
        }

        SyncModeUi();
    }

    private void DiscountType_Changed(object? sender, RoutedEventArgs e) => SyncModeUi();

    private void SyncModeUi()
    {
        var isPercent = RbPercent.IsChecked == true;
        DiscountMode = isPercent ? "percent" : "sum";
        ValueLabel.Text = isPercent ? "Введите процент скидки" : "Введите сумму скидки";
    }

    private void Apply_Click(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;
        ErrorText.Text = "";
        SyncModeUi();

        var raw = (ValueBox.Text ?? "").Trim();
        string? error = null;

        if (decimal.TryParse(raw, out var val))
        {
            if (DiscountMode == "percent")
            {
                if (val < 0 || val > 100)
                    error = "Процент должен быть от 0 до 100";
            }
            else if (val < 0)
            {
                error = "Сумма не может быть отрицательной";
            }
        }
        else
        {
            error = "Введите корректное число";
        }

        if (error != null)
        {
            ErrorText.Text = error;
            ErrorText.IsVisible = true;
            return;
        }

        DiscountValue = val.ToString(CultureInfo.InvariantCulture);
        ClearRequested = false;
        Close(true);
    }

    private void Clear_Click(object? sender, RoutedEventArgs e)
    {
        ClearRequested = true;
        DiscountValue = "";
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
