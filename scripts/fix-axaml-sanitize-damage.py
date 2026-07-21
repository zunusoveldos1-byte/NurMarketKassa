#!/usr/bin/env python3
"""Bulk-fix sanitize damage in Avalonia AXAML files."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path("src/NurMarketKassa.Avalonia")

# Property prefixes wrongly rewritten FrameworkElement/UIElement -> Control
PROP_FIXES = [
    (re.compile(r'\{TemplateBinding\s+Control\.'), "{TemplateBinding "),
    (re.compile(r'Property="Control\.'), 'Property="'),
    (re.compile(r'TemplateBinding\s+Control\.'), "TemplateBinding "),
    # RelativeSource self ActualWidth bindings are WPF-only; strip to fixed width later if needed
]

# WPF-only attributes to strip (name="value")
STRIP_ATTRS = [
    r'ItemContainerStyle\s*=\s*"[^"]*"',
    r'VerticalScrollBarVisibility\s*=\s*"[^"]*"',
    r'HorizontalScrollBarVisibility\s*=\s*"[^"]*"',
    r'SelectedDateChanged\s*=\s*"[^"]*"',
    r'ScrollViewer\.(?:Vertical|Horizontal)ScrollBarVisibility\s*=\s*"[^"]*"',
    r'UIElement\.',
]

changed = 0
for path in ROOT.rglob("*.axaml"):
    if not path.is_file():
        continue
    text = path.read_text(encoding="utf-8")
    orig = text
    for pat, repl in PROP_FIXES:
        text = pat.sub(repl, text)
    for pat in STRIP_ATTRS:
        text = re.sub(pat, "", text)
    # TargetType="UIElement" -> TargetType="Control" is wrong for Opacity; remove such setters
    text = text.replace('TargetType="UIElement"', 'TargetType="Control"')
    text = text.replace("TargetType='UIElement'", "TargetType='Control'")
    # Control.Cursor -> Cursor
    text = text.replace('Property="Control.Cursor"', 'Property="Cursor"')
    if text != orig:
        path.write_text(text, encoding="utf-8", newline="\n")
        changed += 1
        print(path)
print(f"changed={changed}")
