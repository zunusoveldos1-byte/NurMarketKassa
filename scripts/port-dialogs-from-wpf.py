#!/usr/bin/env python3
"""Port WPF Views/Dialogs to Avalonia Views.axaml/Dialogs (basic XAML→AXAML)."""
from __future__ import annotations

import re
import shutil
from pathlib import Path

SRC = Path("src/NurMarketKassa/Views/Dialogs")
DST = Path("src/NurMarketKassa.Avalonia/Views.axaml/Dialogs")
DST.mkdir(parents=True, exist_ok=True)

XAML_REPLACES = [
    ('xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
     'xmlns="https://github.com/avaloniaui"'),
    ("xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"",
     "xmlns=\"https://github.com/avaloniaui\""),
    ("http://schemas.microsoft.com/winfx/2006/xaml/presentation",
     "https://github.com/avaloniaui"),
    ("clr-namespace:", "using:"),
    ("ShowInTaskbar=", "ShowInTaskbar="),  # keep
]

CS_REPLACES = [
    ("using System.Windows;", "// using System.Windows;"),
    ("using System.Windows.Controls;", "// using System.Windows.Controls;"),
    ("using System.Windows.Input;", "using Avalonia.Input;"),
    ("using System.Windows.Media;", "using Avalonia.Media;"),
    ("using System.Windows.Threading;", "// using System.Windows.Threading;"),
    ("MessageBox.", "PosMessageBox."),
    ("Visibility.Collapsed", "false /* Visibility.Collapsed */"),
    ("Visibility.Visible", "true /* Visibility.Visible */"),
]


def convert_xaml(text: str) -> str:
    for a, b in XAML_REPLACES:
        text = text.replace(a, b)
    # Window class namespace stay AvaloniaHost
    text = re.sub(
        r'x:Class="NurMarketKassa\.Views(\.Dialogs)?\.',
        'x:Class="NurMarketKassa.AvaloniaHost.Views.Dialogs.',
        text,
    )
    text = text.replace(
        'x:Class="NurMarketKassa.Views.',
        'x:Class="NurMarketKassa.AvaloniaHost.Views.',
    )
    # Style TargetType="x:Type Button" patterns often OK
    text = text.replace("ClickMode=\"Press\"", "")
    text = text.replace("AllowsTransparency=\"True\"", "")
    text = text.replace("WindowStyle=\"None\"", "SystemDecorations=\"None\"")
    text = text.replace("ResizeMode=\"NoResize\"", "CanResize=\"False\"")
    text = text.replace("ResizeMode=\"CanResizeWithGrip\"", "CanResize=\"True\"")
    text = text.replace("ResizeMode=\"CanMinimize\"", "CanResize=\"False\"")
    text = text.replace("ToolTip.Tip=", "Tip=")
    text = text.replace("ToolTip=", "Tip=")
    return text


def convert_cs(text: str) -> str:
    for a, b in CS_REPLACES:
        text = text.replace(a, b)
    text = re.sub(
        r"namespace\s+NurMarketKassa\.Views(\.Dialogs)?",
        "namespace NurMarketKassa.AvaloniaHost.Views.Dialogs",
        text,
    )
    if "using Avalonia.Controls;" not in text:
        text = "using Avalonia.Controls;\nusing Avalonia.Interactivity;\n" + text
    return text


copied = 0
for src in sorted(SRC.iterdir()):
    if not src.is_file():
        continue
    name = src.name
    if name.endswith(".xaml"):
        dst = DST / (name[:-5] + ".axaml")
        dst.write_text(convert_xaml(src.read_text(encoding="utf-8")), encoding="utf-8", newline="\n")
        copied += 1
    elif name.endswith(".xaml.cs"):
        dst = DST / (name.replace(".xaml.cs", ".axaml.cs"))
        dst.write_text(convert_cs(src.read_text(encoding="utf-8")), encoding="utf-8", newline="\n")
        copied += 1
    elif name.endswith(".cs"):
        # helpers: PosDialogHost, etc.
        dst = DST / name
        dst.write_text(convert_cs(src.read_text(encoding="utf-8")), encoding="utf-8", newline="\n")
        copied += 1

print(f"ported {copied} files to {DST}")
print("dest count", len(list(DST.glob('*'))))
