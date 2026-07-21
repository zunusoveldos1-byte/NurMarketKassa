#!/usr/bin/env python3
"""Aggressive AXAML cleanup for Avalonia compile."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path("src/NurMarketKassa.Avalonia")

TEMPLATE_SETTER = re.compile(
    r'<Setter\s+Property="Template"\s*>\s*<Setter\.Value>\s*<ControlTemplate[\s\S]*?</ControlTemplate>\s*</Setter\.Value>\s*</Setter>',
    re.MULTILINE,
)

TEXT_REPLACES = [
    ("{TemplateBinding Control.", "{TemplateBinding "),
    ('Property="Control.', 'Property="'),
    ('TargetType="UIElement"', 'TargetType="Control"'),
    ("<TabPanel ", "<ItemsPresenter "),
    ("</TabPanel>", "</ItemsPresenter>"),
    ('Property="Border.CornerRadius"', 'Property="CornerRadius"'),
    ('Property="Border.Padding"', 'Property="Padding"'),
    ('Property="DataGrid.RowHeight"', 'Property="RowHeight"'),
    ('Property="DataGrid.GridLinesVisibility"', 'Property="GridLinesVisibility"'),
]

ATTR_REMOVES = [
    re.compile(r'\sItemContainerStyle\s*=\s*"[^"]*"'),
    re.compile(r'\sSelectedDateChanged\s*=\s*"[^"]*"'),
    re.compile(r'\sVerticalScrollBarVisibility\s*=\s*"[^"]*"'),
    re.compile(r'\sHorizontalScrollBarVisibility\s*=\s*"[^"]*"'),
    re.compile(r'\sActualWidth\s*=\s*"[^"]*"'),
]

DROP_SETTERS = [
    re.compile(r'\s*<Setter\s+Property="UIElement\.[^"]+"\s+Value="[^"]*"\s*/>\s*'),
    re.compile(r'\s*<Setter\s+Property="Effect"\s+Value="[^"]*"\s*/>\s*'),
    re.compile(r'\s*<Setter\s+Property="ScrollViewer\.[^"]+"\s+Value="[^"]*"\s*/>\s*'),
    re.compile(r'\s*<Setter\s+Property="DataGrid\.[^"]+"\s+Value="[^"]*"\s*/>\s*'),
]

DROP_TEMPLATE_TYPES = ("TextBox", "DatePicker", "ComboBox", "TabControl", "PasswordBox")


def strip_bad_templates(text: str) -> str:
    def repl(m: re.Match) -> str:
        block = m.group(0)
        for t in DROP_TEMPLATE_TYPES:
            if f'TargetType="{t}"' in block:
                return ""
        return block

    return TEMPLATE_SETTER.sub(repl, text)


changed = 0
for path in ROOT.rglob("*.axaml"):
    if not path.is_file():
        continue
    text = path.read_text(encoding="utf-8")
    orig = text
    for a, b in TEXT_REPLACES:
        text = text.replace(a, b)
    text = strip_bad_templates(text)
    for pat in DROP_SETTERS:
        text = pat.sub("\n", text)
    for pat in ATTR_REMOVES:
        text = pat.sub("", text)
    if text != orig:
        path.write_text(text, encoding="utf-8", newline="\n")
        changed += 1
        print(path)

print(f"changed={changed}")
