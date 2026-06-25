using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

#nullable disable

using NurMarketKassa.Views;

namespace NurMarketKassa.Views.Dialogs {
    public partial class CashHistoryDialog : Window
    {
        public CashHistoryDialog(ObservableCollection<FinanceWindow.CashSessionEntry> allSessions, string currentUserId)
        {
            InitializeComponent();

            var filtered = string.IsNullOrWhiteSpace(currentUserId)
                ? allSessions.ToList()
                : allSessions.Where(s => s.UserId == currentUserId).ToList();

            HistoryGrid.ItemsSource = new ObservableCollection<FinanceWindow.CashSessionEntry>(filtered);
        }
    }
}