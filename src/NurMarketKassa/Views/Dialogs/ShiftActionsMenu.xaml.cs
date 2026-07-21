using System.Windows;
using System.Windows.Controls;
using NurMarketKassa.ViewModels;

namespace NurMarketKassa.Views.Dialogs;

public partial class ShiftActionsMenu
{
    public ShiftMenuAction? SelectedAction { get; private set; }

    public ShiftActionsMenu()
    {
        InitializeComponent();
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ShiftMenuAction action })
        {
            SelectedAction = action;
            CloseWithAnimation(true);
        }
    }
}
