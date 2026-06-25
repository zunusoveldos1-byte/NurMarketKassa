using System.Globalization;
using System.Windows;
using NurMarketKassa.Services;

namespace NurMarketKassa.Views.Dialogs;

public partial class CloseShiftDialog : AppOverlayDialogBase
{
    public decimal? ClosingCash =>
        decimal.TryParse(ClosingCashBox.Text, out var value) ? value : null;

    public decimal? SuggestedBalance { get; set; }

    public CloseShiftDialog()
    {
        InitializeComponent();
        Loaded += OnDialogContentLoaded;
    }

    private void OnDialogContentLoaded(object sender, RoutedEventArgs e)
    {
        if (SuggestedBalance.HasValue)
        {
            SystemBalanceText.Text = ShiftBalanceHelper.FormatBalance(SuggestedBalance);
            ClosingCashBox.Text = SuggestedBalance.Value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        ClosingCashBox.Focus();
        ClosingCashBox.SelectAll();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ClosingCashBox.Text) &&
            !decimal.TryParse(ClosingCashBox.Text, out _))
        {
            PosMessageBox.Show("Введите корректную сумму.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CloseWithAnimation(true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => CloseWithAnimation(false);
}
