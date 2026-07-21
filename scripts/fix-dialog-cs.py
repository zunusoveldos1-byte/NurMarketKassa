#!/usr/bin/env python3
"""Fix freshly ported dialog code-behind for Avalonia."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path("src/NurMarketKassa.Avalonia/Views.axaml/Dialogs")

REPLACES = [
    ("using NurMarketKassa.Views.Dialogs;", "using NurMarketKassa.AvaloniaHost.Views.Dialogs;"),
    ("using NurMarketKassa.Views;", "using NurMarketKassa.AvaloniaHost.Views;"),
    ("namespace NurMarketKassa.Views.Dialogs", "namespace NurMarketKassa.AvaloniaHost.Views.Dialogs"),
    ("namespace NurMarketKassa.Views", "namespace NurMarketKassa.AvaloniaHost.Views"),
    ("override void OnSourceInitialized", "void OnSourceInitialized_Unused"),
    ("protected override void OnSourceInitialized", "protected void OnSourceInitialized_Unused"),
    ("FrmKeyboardViewModel", "global::NurMarketKassa.AvaloniaHost.ViewModels.FrmKeyboardViewModel"),
]

# Avoid double-globalizing
UNDO = [
    ("global::global::NurMarketKassa.AvaloniaHost.ViewModels.FrmKeyboardViewModel",
     "global::NurMarketKassa.AvaloniaHost.ViewModels.FrmKeyboardViewModel"),
]

for path in ROOT.rglob("*.cs"):
    if not path.is_file():
        continue
    text = path.read_text(encoding="utf-8")
    orig = text
    for a, b in REPLACES:
        text = text.replace(a, b)
    for a, b in UNDO:
        text = text.replace(a, b)
    # Ensure Avalonia usings
    if "using Avalonia.Controls;" not in text:
        text = "using Avalonia.Controls;\nusing Avalonia.Interactivity;\n" + text
    if text != orig:
        path.write_text(text, encoding="utf-8", newline="\n")
        print("fixed", path.name)

print("done")
