#!/usr/bin/env python3
"""Restore System.Windows usings that the port script commented out / replaced."""
from pathlib import Path

ROOT = Path("src/NurMarketKassa.Avalonia/Views.axaml/Dialogs")

for path in ROOT.rglob("*.cs"):
    if not path.is_file():
        continue
    text = path.read_text(encoding="utf-8")
    orig = text
    text = text.replace("// using System.Windows;", "using System.Windows;")
    text = text.replace("// using System.Windows.Controls;", "using System.Windows.Controls;")
    text = text.replace("// using System.Windows.Threading;", "using System.Windows.Threading;")
    # Ensure both Avalonia.Input and System.Windows.Input when needed
    if "MouseButtonEventArgs" in text or "TextCompositionEventArgs" in text or "KeyEventArgs" in text:
        if "using System.Windows.Input;" not in text:
            text = "using System.Windows.Input;\n" + text
    if "DependencyObject" in text or "DependencyProperty" in text or "UIElement" in text or "FrameworkElement" in text or "Duration" in text:
        if "using System.Windows;" not in text:
            text = "using System.Windows;\n" + text
    if "DispatcherTimer" in text and "using System.Windows.Threading;" not in text:
        text = "using System.Windows.Threading;\n" + text
    if "ControlTemplate" in text and "using System.Windows.Controls;" not in text:
        text = "using System.Windows.Controls;\n" + text
    if text != orig:
        path.write_text(text, encoding="utf-8", newline="\n")
        print("restored", path.name)

# Fix ShiftHistoryViewModel Models using
vm = Path("src/NurMarketKassa.Avalonia/ViewModels/ShiftHistoryViewModel.cs")
if vm.exists():
    t = vm.read_text(encoding="utf-8")
    t2 = t.replace("using NurMarketKassa.AvaloniaHost.Models;", "using NurMarketKassa.Services;\nusing NurMarketKassa.ViewModels;")
    if t2 != t:
        vm.write_text(t2, encoding="utf-8", newline="\n")
        print("fixed ShiftHistoryViewModel usings")

print("done")
