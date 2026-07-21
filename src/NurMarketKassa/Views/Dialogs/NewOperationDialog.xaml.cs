using System.Globalization;
using System.Windows;
using NurMarketKassa.Models;
using NurMarketKassa.Services;

namespace NurMarketKassa.Views.Dialogs;

public partial class NewOperationDialog
{
    private bool _isDeposit = true;

    public CashOperationModel? ResultOperation { get; private set; }

    public NewOperationDialog() : this(isDeposit: true) { }

    public NewOperationDialog(bool isDeposit)
    {
        _isDeposit = isDeposit;
        InitializeComponent();
        DepositTab.IsChecked = isDeposit;
        WithdrawTab.IsChecked = !isDeposit;
        DateBox.SelectedDate = DateTime.Today;
        CashierBox.Items.Add(App.CurrentUserId ?? "Кассир 1");
        CashierBox.SelectedIndex = 0;
    }

    private void Tab_Changed(object sender, RoutedEventArgs e) =>
        _isDeposit = DepositTab.IsChecked == true;

    private void Close_Click(object sender, RoutedEventArgs e) =>
        CloseWithAnimation(false);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!decimal.TryParse(AmountBox.Text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0)
        {
            PosMessageBox.Show((Window)this, "Укажите корректную сумму.", "Новая операция",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var type = _isDeposit ? "Внесение" : "Изъятие";
        var createdAt = DateBox.SelectedDate?.Date.Add(DateTime.Now.TimeOfDay) ?? DateTime.Now;
        var cashier = CashierBox.SelectedItem?.ToString() ?? App.CurrentUserId ?? "—";
        var reason = ReasonBox.Text.Trim();
        var comment = CommentBox.Text.Trim();
        var note = string.IsNullOrWhiteSpace(comment) ? reason : comment;

        ResultOperation = new CashOperationModel
        {
            CreatedAt = createdAt,
            Type = type,
            Kind = CashOperationModel.ResolveKind(type),
            Amount = amount,
            Cashier = cashier,
            Reason = reason,
            Comment = note,
        };

        CloseWithAnimation(true);
    }
}
