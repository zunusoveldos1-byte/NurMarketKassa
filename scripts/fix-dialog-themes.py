#!/usr/bin/env python3
from pathlib import Path

d = Path("src/NurMarketKassa.Avalonia/Views.axaml/Dialogs")
for name in ["PosDialogTheme", "NurMarketDialogTheme", "PosDialogStyles"]:
    for ext in [".axaml", ".axaml.cs"]:
        p = d / f"{name}{ext}"
        if p.exists():
            p.unlink()
            print("del", p.name)

rd = """<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
</ResourceDictionary>
"""
for name in ["PosDialogTheme", "NurMarketDialogTheme", "PosDialogStyles"]:
    (d / f"{name}.axaml").write_text(rd, encoding="utf-8", newline="\n")
    print("wrote", name)
