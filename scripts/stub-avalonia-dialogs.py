#!/usr/bin/env python3
"""Replace broken WPF-ported dialogs with minimal Avalonia Window stubs that compile."""
from __future__ import annotations

import re
import shutil
from pathlib import Path

DST = Path("src/NurMarketKassa.Avalonia/Views.axaml/Dialogs")
SRC = Path("src/NurMarketKassa/Views/Dialogs")
BACKUP = Path("src/NurMarketKassa.Avalonia/Views.axaml/_dialog_wpf_port_broken")

# Wipe current dialogs (keep nothing broken)
if DST.exists():
    if BACKUP.exists():
        shutil.rmtree(BACKUP)
    BACKUP.mkdir(parents=True, exist_ok=True)
    for p in DST.iterdir():
        if p.is_file():
            shutil.move(str(p), BACKUP / p.name)

DST.mkdir(parents=True, exist_ok=True)

AXAML_TMPL = """<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="NurMarketKassa.AvaloniaHost.Views.Dialogs.{name}"
        Title="{title}"
        Width="640" Height="480"
        WindowStartupLocation="CenterOwner"
        CanResize="True">
  <Grid Margin="16">
    <TextBlock Text="{title}" FontSize="18" FontWeight="SemiBold"
               HorizontalAlignment="Center" VerticalAlignment="Center"/>
  </Grid>
</Window>
"""

CS_TMPL = """using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public partial class {name} : Window
{{
    public {name}()
    {{
        InitializeComponent();
    }}

{extra}
}}
"""

# Public members commonly needed — add stubs so call sites compile.
EXTRA: dict[str, str] = {
    "CheckoutDialog": """
    public bool? DialogResult { get; set; }
    public decimal PaidAmount { get; set; }
""",
    "PosAlertDialog": """
    public static void Show(Window? owner, string title, string message) { }
    public static Task ShowAsync(Window? owner, string title, string message) => Task.CompletedTask;
""",
    "PosConfirmDialog": """
    public static bool Confirm(Window? owner, string title, string message) => false;
    public static Task<bool> ConfirmAsync(Window? owner, string title, string message) => Task.FromResult(false);
""",
    "SaleSuccessDialog": """
    public static void Show(Window? owner, string message) { }
""",
    "PrinterNotConnectedDialog": """
    public static NurMarketKassa.Ui.Shared.PrinterNotConnectedResult Show(Window? owner)
        => NurMarketKassa.Ui.Shared.PrinterNotConnectedResult.Cancel;
""",
    "PaymentConfirmationDialog": """
    public static bool Confirm(Window? owner) => false;
""",
    "OpenShiftDialog": """
    public decimal OpeningCash { get; set; }
    public bool? DialogResult { get; set; }
""",
    "CloseShiftDialog": """
    public decimal ClosingCash { get; set; }
    public bool? DialogResult { get; set; }
""",
    "ProductDetailDialog": """
    public ProductDetailDialog(object? product) { InitializeComponent(); }
""",
    "DeferredCartsDialog": """
    public bool? DialogResult { get; set; }
""",
    "CashOperationsDialog": """
    public bool? DialogResult { get; set; }
""",
    "ReturnSaleDialog": """
    public bool? DialogResult { get; set; }
""",
    "FrmKeyboard": """
    public string? ResultText { get; set; }
    public static bool IsShift { get; set; }
""",
    "ShiftActionsMenu": """
    public NurMarketKassa.ViewModels.ShiftMenuAction? SelectedAction { get; set; }
""",
    "NewOperationDialog": """
    public bool? DialogResult { get; set; }
""",
    "ShiftDetailsDialog": """
    public bool? DialogResult { get; set; }
""",
    "NoStockDialog": "",
    "WeighedProductDialog": """
    public double? WeightKg { get; set; }
    public bool? DialogResult { get; set; }
""",
    "OrderDiscountDialog": """
    public decimal? DiscountValue { get; set; }
    public bool? DialogResult { get; set; }
""",
    "ExitConfirmationDialog": """
    public bool? DialogResult { get; set; }
""",
    "ReceiptPreviewDialog": "",
    "CashHistoryDialog": "",
    "FinanceDateRangeDialog": "",
    "PaymentStockBlockedDialog": "",
    "DeferredStockIssuesDialog": "",
    "ReturnLineReasonDialog": """
    public string? Reason { get; set; }
""",
    "ShiftNotClosedDialog": "",
    "PosDialogHost": "",
}

# Also helpers as plain CS without AXAML
HELPERS = {
    "PosDialogHost.cs": """using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public static class PosDialogHost
{
    public static async Task<T?> ShowDialogAsync<T>(Window owner, Window dialog) where T : class
    {
        var result = await dialog.ShowDialog<T>(owner);
        return result;
    }

    public static Task ShowAsync(Window owner, Window dialog) => dialog.ShowDialog(owner);
}
""",
    "PosModalHost.cs": """using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public static class PosModalHost
{
    public static Task ShowAsync(Window owner, Window dialog) => dialog.ShowDialog(owner);
}
""",
    "PosDialogWindowBase.cs": """using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public class PosDialogWindowBase : Window
{
    public PosDialogWindowBase() { }
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
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Avalonia.Media.Brushes.Transparent;
    }
}
""",
    "CustomDialogWindow.cs": """using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

public class CustomDialogWindow : Window
{
    public CustomDialogWindow() { }
}
""",
    "PosDialogLayout.cs": """using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

internal static class PosDialogLayout
{
    public static void AttachOverlayToOwner(Window dialog, Window owner) { }
    public static void FitOverlayToOwner(Window dialog, Window? owner) { }
}
""",
    "PosDialogUi.cs": """using Avalonia.Controls;

namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

internal static class PosDialogUi
{
}
""",
    "DeferredCartsDialogActions.cs": """namespace NurMarketKassa.AvaloniaHost.Views.Dialogs;

internal static class DeferredCartsDialogActions
{
}
""",
}

created = 0
for xaml in sorted(SRC.glob("*.xaml")):
    name = xaml.stem
    title = name
    (DST / f"{name}.axaml").write_text(
        AXAML_TMPL.format(name=name, title=title), encoding="utf-8", newline="\n"
    )
    extra = EXTRA.get(name, "    public bool? DialogResult { get; set; }\n")
    (DST / f"{name}.axaml.cs").write_text(
        CS_TMPL.format(name=name, extra=extra), encoding="utf-8", newline="\n"
    )
    created += 1

for fname, content in HELPERS.items():
    (DST / fname).write_text(content, encoding="utf-8", newline="\n")

print(f"created {created} dialog stubs + {len(HELPERS)} helpers")
print("dialog files", len(list(DST.glob('*'))))
