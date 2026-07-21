using System.Windows;
using System.Windows.Input;
using NurMarketKassa.ViewModels;

namespace NurMarketKassa.Views;

public partial class ShiftsHistoryWindow : Window
{
    public ShiftsHistoryWindow()
    {
        InitializeComponent();
        DataContext = new ShiftHistoryViewModel(this);
        WindowState = WindowState.Maximized;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
            DragMove();
    }
}
