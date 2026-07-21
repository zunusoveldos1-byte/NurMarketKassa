#!/usr/bin/env python3
from pathlib import Path

files = [
    Path("src/NurMarketKassa.Avalonia/Views.axaml/Dialogs/CustomDialogWindow.cs"),
    Path("src/NurMarketKassa.Avalonia/Views.axaml/Dialogs/PosDialogUi.cs"),
    Path("src/NurMarketKassa.Avalonia/Views.axaml/Dialogs/AppOverlayDialogBase.cs"),
    Path("src/NurMarketKassa.Avalonia/Views.axaml/Dialogs/WeighedProductDialog.axaml.cs"),
    Path("src/NurMarketKassa.Avalonia/Views.axaml/Dialogs/PosDialogLayout.cs"),
    Path("src/NurMarketKassa.Avalonia/Views.axaml/Dialogs/FrmKeyboard.axaml.cs"),
]

for path in files:
    if not path.exists():
        continue
    text = path.read_text(encoding="utf-8")
    orig = text
    # Drop ambiguous WPF Controls using; keep Avalonia via global usings
    text = text.replace("using System.Windows.Controls;\n", "")
    # Prefer Avalonia KeyEventArgs
    if "KeyEventArgs" in text and "using Avalonia.Input;" not in text:
        text = "using Avalonia.Input;\n" + text
    # Qualify Rect from Input shims
    if "Rect " in text or "Rect(" in text or "-> Rect" in text or ": Rect" in text:
        if "using System.Windows.Input;" not in text:
            text = "using System.Windows.Input;\n" + text
    if text != orig:
        path.write_text(text, encoding="utf-8", newline="\n")
        print("fixed", path.name)
print("done")
