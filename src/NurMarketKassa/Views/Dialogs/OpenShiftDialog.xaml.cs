using System.Globalization;
using System.Windows;
using NurMarketKassa.Services;

namespace NurMarketKassa.Views.Dialogs;

public partial class OpenShiftDialog : AppOverlayDialogBase
{
    public decimal OpeningCash =>
        decimal.TryParse(OpeningCashBox.Text, out var value) ? value : 0;

    public decimal? SuggestedBalance { get; set; }

    public OpenShiftDialog()
    {
        InitializeComponent();
        Loaded += OnDialogContentLoaded;
    }

    private void OnDialogContentLoaded(object sender, RoutedEventArgs e)
    {
        if (SuggestedBalance.HasValue)
        {
            SystemBalanceText.Text = ShiftBalanceHelper.FormatBalance(SuggestedBalance);
            OpeningCashBox.Text = SuggestedBalance.Value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        OpeningCashBox.Focus();
        OpeningCashBox.SelectAll();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(OpeningCashBox.Text, out _))
        {
            PosMessageBox.Show("Введите корректную сумму.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        CloseWithAnimation(true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => CloseWithAnimation(false);
}
