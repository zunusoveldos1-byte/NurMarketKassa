using System.Windows;

namespace NurMarketKassa.Views;

public enum ReturnReasonDialogKind
{
    /// <summary>Позиции по одной или несколько с одной причиной.</summary>
    LineItems,
    /// <summary>Весь чек целиком.</summary>
    FullReceipt,
}

public partial class ReturnLineReasonDialog : Window
{
    public ReturnLineReasonDialog(int selectedItemCount = 1, ReturnReasonDialogKind kind = ReturnReasonDialogKind.LineItems)
    {
        InitializeComponent();
        if (kind == ReturnReasonDialogKind.FullReceipt)
        {
            Title = "Причина полного возврата";
            HintText.Text = "Укажите причину. Она будет передана в CRM для возврата всего чека целиком.";
        }
        else
        {
            Title = "Причина возврата";
            HintText.Text = selectedItemCount > 1
                ? $"Одна и та же причина будет указана для {selectedItemCount} выбранных позиций и передана в CRM."
                : "Комментарий будет передан в CRM вместе с возвратом позиции.";
        }

        Loaded += (_, _) => ReasonBox.Focus();
    }

    public string ReasonText => (ReasonBox.Text ?? "").Trim();

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        if (ReasonText.Length == 0)
        {
            ErrorText.Text = "Введите причину возврата.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
