using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class ReturnLineReasonDialog : Window
{
    public ReturnLineReasonDialog()
    {
        InitializeComponent();
    }


    public string? Reason { get; set; }

}
