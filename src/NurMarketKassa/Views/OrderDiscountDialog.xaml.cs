using System.Windows;
using NurMarketKassa.Services;

namespace NurMarketKassa.Views;

public partial class OrderDiscountDialog : Window
{
    public string DiscountMode { get; private set; } = "percent";
    public string DiscountValue { get; private set; } = "";
    public bool ClearRequested { get; private set; }

    public OrderDiscountDialog(string? currentPercent, string? currentSum)
    {
        InitializeComponent();

        var pct = (currentPercent ?? "").Trim();
        var sum = (currentSum ?? "").Trim();

        CurrentDiscountText.Text = !string.IsNullOrEmpty(pct)
            ? $"Сейчас действует скидка: {pct}%"
            : !string.IsNullOrEmpty(sum)
                ? $"Сейчас действует скидка: {sum} сом"
                : "Сейчас скидка на чек не задана.";

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
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    private void DiscountType_Changed(object sender, RoutedEventArgs e) => SyncModeUi();

    private void SyncModeUi()
    {
        if (RbPercent is null || RbSum is null || ValueLabel is null)
            return;

        var percent = RbPercent.IsChecked == true;
        DiscountMode = percent ? "percent" : "sum";
        ValueLabel.Text = percent ? "Введите процент скидки" : "Введите сумму скидки";
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        ErrorText.Text = "";
        SyncModeUi();

        var raw = (ValueBox.Text ?? "").Trim();
        string? err = DiscountMode == "percent"
            ? OrderDiscountHelper.ValidatePercent(raw)
            : OrderDiscountHelper.ValidateSum(raw);

        if (err != null)
        {
            ErrorText.Text = err;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DiscountValue = OrderDiscountHelper.NormalizeDecimal(raw);
        ClearRequested = false;
        DialogResult = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ClearRequested = true;
        DiscountValue = "";
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
