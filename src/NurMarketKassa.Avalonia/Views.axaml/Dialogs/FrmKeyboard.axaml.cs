using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class FrmKeyboard : Window
{
    public static FrmKeyboard? CurrentForm { get; private set; }
    public string? ResultText { get; set; }
    public static bool IsShift { get; set; }

    public FrmKeyboard()
    {
        InitializeComponent();
        CurrentForm = this;
    }

    public static void ShowKeyboard(Window? owner = null, bool hideLetters = false)
    {
        CurrentForm ??= new FrmKeyboard();
    }

    public static void KillKeyboard()
    {
        CurrentForm?.Close();
        CurrentForm = null;
    }
}
