#!/usr/bin/env python3
"""Expand dialog stub APIs to satisfy Avalonia call sites."""
from pathlib import Path

DST = Path("src/NurMarketKassa.Avalonia/Views.axaml/Dialogs")

files = {
"FrmKeyboard.axaml.cs": """using Avalonia.Controls;

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
""",
"PosDialogHost.cs": """using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public static class PosDialogHost
{
    public static bool? Show(Window owner, Window dialog)
    {
        dialog.ShowDialog(owner);
        return true;
    }

    public static async Task<T?> ShowDialogAsync<T>(Window owner, Window dialog) where T : class
    {
        return await dialog.ShowDialog<T>(owner);
    }

    public static Task ShowAsync(Window owner, Window dialog) => dialog.ShowDialog(owner);
}
""",
"OpenShiftDialog.axaml.cs": """using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class OpenShiftDialog : Window
{
    public decimal OpeningCash { get; set; }
    public decimal SuggestedBalance { get; set; }
    public bool? DialogResult { get; set; }

    public OpenShiftDialog() => InitializeComponent();
}
""",
"CloseShiftDialog.axaml.cs": """using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class CloseShiftDialog : Window
{
    public decimal ClosingCash { get; set; }
    public decimal SuggestedBalance { get; set; }
    public bool? DialogResult { get; set; }

    public CloseShiftDialog() => InitializeComponent();
}
""",
"NewOperationDialog.axaml.cs": """using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class NewOperationDialog : Window
{
    public object? ResultOperation { get; set; }
    public bool? DialogResult { get; set; }

    public NewOperationDialog() => InitializeComponent();
    public NewOperationDialog(object? arg) : this() { }
}
""",
"ShiftDetailsDialog.axaml.cs": """using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class ShiftDetailsDialog : Window
{
    public bool? DialogResult { get; set; }

    public ShiftDetailsDialog() => InitializeComponent();
    public ShiftDetailsDialog(object? model) : this() { }
}
""",
"ShiftActionsMenu.axaml.cs": """using Avalonia.Controls;
using NurMarketKassa.ViewModels;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class ShiftActionsMenu : Window
{
    public ShiftMenuAction? SelectedAction { get; set; }
    public bool? DialogResult { get; set; }

    public ShiftActionsMenu() => InitializeComponent();
    public ShiftActionsMenu(object? context) : this() { }
}
""",
"ReceiptPreviewDialog.axaml.cs": """using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class ReceiptPreviewDialog : Window
{
    public ReceiptPreviewDialog() => InitializeComponent();
    public ReceiptPreviewDialog(string content) : this() { }
    public ReceiptPreviewDialog(object? a, object? b) : this() { }
}
""",
"AppOverlayDialogBase.cs": """using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public class AppOverlayDialogBase : Window
{
    public AppOverlayDialogBase()
    {
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        Background = Avalonia.Media.Brushes.Transparent;
    }

    protected void CloseWithAnimation() => Close();
}
""",
}

# FilterWindow needs CloseWithAnimation - add extension or base
filter_cs = Path("src/NurMarketKassa.Avalonia/Views.axaml/Main/FilterWindow.axaml.cs")
if filter_cs.exists():
    t = filter_cs.read_text(encoding="utf-8")
    if "CloseWithAnimation" in t and "void CloseWithAnimation" not in t:
        # inject method into class
        t = t.replace("public partial class FilterWindow : Window\n{",
                      "public partial class FilterWindow : Window\n{\n    private void CloseWithAnimation() => Close();\n")
        if "private void CloseWithAnimation" not in t:
            t = t.replace("public partial class FilterWindow : Window {",
                          "public partial class FilterWindow : Window {\n    private void CloseWithAnimation() => Close();")
        filter_cs.write_text(t, encoding="utf-8", newline="\n")
        print("patched FilterWindow")

for name, content in files.items():
    (DST / name).write_text(content, encoding="utf-8", newline="\n")
    print("wrote", name)

# LoginWindow Show(Window) - Avalonia uses Show(Window? owner) differently
login = Path("src/NurMarketKassa.Avalonia/Views.axaml/Login/LoginWindow.axaml.cs")
if login.exists():
    t = login.read_text(encoding="utf-8")
    t2 = t.replace(".Show(this);", ".Show();")
    # also Application ambiguity
    if "using Application = Avalonia.Application;" not in t2:
        t2 = "using Application = Avalonia.Application;\n" + t2
    if t2 != t:
        login.write_text(t2, encoding="utf-8", newline="\n")
        print("patched LoginWindow")

print("done")
