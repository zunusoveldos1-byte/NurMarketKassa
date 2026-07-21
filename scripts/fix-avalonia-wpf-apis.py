#!/usr/bin/env python3
"""Bulk-fix common WPF APIs in Avalonia Views.axaml code-behind."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "src" / "NurMarketKassa.Avalonia" / "Views.axaml"


def fix_cs(text: str) -> str:
    # Visibility property assignments
    text = re.sub(
        r"\.Visibility\s*=\s*Visibility\.Collapsed",
        ".IsVisible = false",
        text,
    )
    text = re.sub(
        r"\.Visibility\s*=\s*Visibility\.Hidden",
        ".IsVisible = false",
        text,
    )
    text = re.sub(
        r"\.Visibility\s*=\s*Visibility\.Visible",
        ".IsVisible = true",
        text,
    )
    # Ternary Visibility
    text = re.sub(
        r"(\w+(?:\.\w+)*)\.Visibility\s*=\s*([^;]+)\s*\?\s*Visibility\.Visible\s*:\s*Visibility\.(?:Collapsed|Hidden)",
        r"\1.IsVisible = \2",
        text,
    )
    text = re.sub(
        r"(\w+(?:\.\w+)*)\.Visibility\s*=\s*([^;]+)\s*\?\s*Visibility\.(?:Collapsed|Hidden)\s*:\s*Visibility\.Visible",
        r"\1.IsVisible = !(\2)",
        text,
    )
    # Comparisons
    text = re.sub(
        r"\.Visibility\s*==\s*Visibility\.Visible",
        ".IsVisible",
        text,
    )
    text = re.sub(
        r"\.Visibility\s*!=\s*Visibility\.Visible",
        ".IsVisible == false",
        text,
    )
    text = re.sub(
        r"\.Visibility\s*==\s*Visibility\.(?:Collapsed|Hidden)",
        ".IsVisible == false",
        text,
    )

    # Window chrome
    text = re.sub(
        r"WindowStyle\s*=\s*WindowStyle\.None",
        "SystemDecorations = SystemDecorations.None",
        text,
    )
    text = re.sub(
        r"ResizeMode\s*=\s*ResizeMode\.NoResize",
        "CanResize = false",
        text,
    )
    text = re.sub(
        r"ResizeMode\s*=\s*ResizeMode\.CanMinimize",
        "CanResize = false",
        text,
    )
    text = re.sub(
        r"ResizeMode\s*=\s*ResizeMode\.CanResize(?:WithGrip)?",
        "CanResize = true",
        text,
    )

    # Drag / dialog result
    text = re.sub(r"\bDragMove\s*\(\s*\)", "BeginMoveDrag(e)", text)
    text = re.sub(r"\bDialogResult\s*=\s*true\s*;", "Close(true);", text)
    text = re.sub(r"\bDialogResult\s*=\s*false\s*;", "Close(false);", text)

    # Application.Current.MainWindow =
    text = re.sub(
        r"Application\.Current\.MainWindow\s*=\s*(\w+)\s*;",
        r"if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime __desk) __desk.MainWindow = \1;",
        text,
    )

    # FindResource → TryGetResource
    text = re.sub(
        r"\((\w+)\)FindResource\(",
        r"(\1)(this.FindResource(",
        text,
    )

    # PasswordBox.Password → TextBox.Text (after PasswordBox becomes TextBox)
    text = re.sub(r"PasswordBox\.Password", "PasswordBox.Text", text)
    text = re.sub(r"PasswordBox\.SelectAll\(\)", "PasswordBox.SelectAll()", text)

    # ShowDialog sync → ShowDialog sync await pattern leftovers
    # Owner = this already fine

    return text


def fix_axaml(text: str) -> str:
    # PasswordBox → TextBox with PasswordChar
    text = re.sub(
        r"<PasswordBox(\s[^>]*)>",
        lambda m: "<TextBox" + m.group(1).replace("PasswordChanged=", "TextChanged=") + ' PasswordChar="•">',
        text,
    )
    text = text.replace("</PasswordBox>", "</TextBox>")
    text = re.sub(
        r'TargetType="PasswordBox"',
        'TargetType="TextBox"',
        text,
    )
    text = re.sub(
        r"TargetType=\"\{x:Type PasswordBox\}\"",
        'TargetType="TextBox"',
        text,
    )

    # FrameworkElement.Resources → Window/UserControl.Resources (generic)
    text = text.replace("FrameworkElement.Resources", "Window.Resources")

    # Visibility="Collapsed" → IsVisible="False" etc in AXAML
    text = re.sub(r'Visibility="Collapsed"', 'IsVisible="False"', text)
    text = re.sub(r'Visibility="Hidden"', 'IsVisible="False"', text)
    text = re.sub(r'Visibility="Visible"', 'IsVisible="True"', text)

    # WPF-only attributes strip
    text = re.sub(r'\s+SnapsToDevicePixels="[^"]*"', "", text)
    text = re.sub(r'\s+UseLayoutRounding="[^"]*"', "", text)
    text = re.sub(r'\s+FocusVisualStyle="[^"]*"', "", text)
    text = re.sub(r'\s+TextOptions\.[A-Za-z]+="[^"]*"', "", text)
    text = re.sub(r'\s+RenderOptions\.[A-Za-z]+="[^"]*"', "", text)
    text = re.sub(r'\s+RecognizesAccessKey="[^"]*"', "", text)
    text = re.sub(r'\s+VerticalContentAlignment="[^"]*"', "", text)
    text = re.sub(r'\s+AllowsTransparency="[^"]*"', "", text)
    text = re.sub(r'\s+WindowStyle="[^"]*"', "", text)
    text = re.sub(r'\s+ResizeMode="[^"]*"', "", text)
    text = re.sub(r'\s+ShowActivated="[^"]*"', "", text)

    # pack:// → avares://
    text = text.replace(
        "pack://application:,,,/Assets/",
        "avares://NurMarketKassa.Avalonia/Assets/",
    )

    return text


def main() -> None:
    changed = 0
    for path in ROOT.rglob("*"):
        if "_wpf_port_backup" in path.parts:
            continue
        if path.suffixes[-2:] == [".axaml", ".cs"] or path.name.endswith(".axaml.cs"):
            if path.suffix != ".cs":
                continue
            if path.name.endswith((".bak", ".wpfbak", ".shellbak", ".Thin.cs")):
                continue
            original = path.read_text(encoding="utf-8", errors="replace")
            updated = fix_cs(original)
            if updated != original:
                path.write_text(updated, encoding="utf-8")
                changed += 1
                print(f"CS  {path.relative_to(ROOT)}")
        elif path.suffix == ".axaml" and not path.name.endswith(".bak"):
            original = path.read_text(encoding="utf-8", errors="replace")
            updated = fix_axaml(original)
            if updated != original:
                path.write_text(updated, encoding="utf-8")
                changed += 1
                print(f"XML {path.relative_to(ROOT)}")
    print(f"Updated {changed} files")


if __name__ == "__main__":
    main()
