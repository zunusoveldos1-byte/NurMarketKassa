using Avalonia.Controls;
using NurMarketKassa.ViewModels;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class ShiftActionsMenu : Window
{
    public ShiftMenuAction? SelectedAction { get; set; }
    public bool? DialogResult { get; set; }

    public ShiftActionsMenu() => InitializeComponent();
    public ShiftActionsMenu(object? context) : this() { }
}
