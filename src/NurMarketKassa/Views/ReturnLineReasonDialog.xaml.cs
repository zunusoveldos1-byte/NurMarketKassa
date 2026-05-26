using System;
using System.Windows;
using System.Windows.Controls;

#nullable disable

namespace NurMarketKassa.Views
{
    public enum ReturnReasonDialogKind
    {
        LineItems,
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
                HintText.Text = selectedItemCount <= 1
                    ? "Комментарий будет передан в CRM вместе с возвратом позиции."
                    : $"Одна и та же причина будет указана для {selectedItemCount} выбранных позиций и передана в CRM.";
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
            }
            else
            {
                DialogResult = true;
            }
        }
    }
}