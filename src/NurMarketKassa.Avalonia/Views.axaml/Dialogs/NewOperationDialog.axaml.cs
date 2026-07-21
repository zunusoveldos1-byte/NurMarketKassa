using Avalonia.Controls;
using NurMarketKassa.Models;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class NewOperationDialog : Window
{
    public CashOperationModel? ResultOperation { get; set; }
    public bool? DialogResult { get; set; }

    public NewOperationDialog() => InitializeComponent();
    public NewOperationDialog(object? arg) : this() { }
}
