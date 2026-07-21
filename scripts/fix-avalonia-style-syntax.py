#!/usr/bin/env python3
from __future__ import annotations
import re
from pathlib import Path

ROOT = Path(r"C:\Users\User\Desktop\NurCrmPosKassa-master\NurCrmPosKassa-master\src\NurMarketKassa.Avalonia")

def fix(text: str) -> str:
    # Avalonia Window/UserControl has no Style= property like WPF
    text = re.sub(r'\s+Style="\{(?:Static|Dynamic)Resource AppWindow\}"', "", text)
    # TargetType="{x:Type Foo}" -> TargetType="Foo"
    text = re.sub(r'TargetType="\{x:Type\s+([^}]+)\}"', r'TargetType="\1"', text)
    # BasedOn with missing bases often breaks; leave for now
    # StrokeStartLineCap etc on Path — Avalonia uses different names; strip unknown WPF path attrs
    for a in [
        r'\s+StrokeStartLineCap="[^"]*"',
        r'\s+StrokeEndLineCap="[^"]*"',
        r'\s+StrokeLineJoin="[^"]*"',
        r'\s+StrokeDashCap="[^"]*"',
        r'\s+StrokeMiterLimit="[^"]*"',
        r'\s+PreviewTextInput="[^"]*"',
        r'\s+PreviewKeyDown="[^"]*"',
        r'\s+PreviewMouse(?:Down|Up|Wheel)="[^"]*"',
        r'\s+MouseDoubleClick="[^"]*"',
        r'\s+IsDefault="[^"]*"',
        r'\s+IsCancel="[^"]*"',
        r'\s+AcceptsReturn="[^"]*"',
        r'\s+CommandTarget="[^"]*"',
        r'\s+ToolTipService\.[A-Za-z]+="[^"]*"',
        r'\s+AutomationProperties\.[A-Za-z]+="[^"]*"',
        r'\s+xml:space="[^"]*"',
    ]:
        text = re.sub(a, "", text)
    # DropShadowEffect Direction not in Avalonia the same way — leave Effect alone
    # Style="{x:Null}" -> remove
    text = re.sub(r'\s+Style="\{x:Null\}"', "", text)
    # Hyperlink remnants
    text = re.sub(r"<Hyperlink(\s[^>]*)?>.*?</Hyperlink>", "", text, flags=re.DOTALL)
    return text

n = 0
for path in ROOT.rglob("*.axaml"):
    if not path.is_file() or "_wpf_port_backup" in path.parts or path.name.endswith(".bak"):
        continue
    o = path.read_text(encoding="utf-8", errors="replace")
    u = fix(o)
    if u != o:
        path.write_text(u, encoding="utf-8", newline="\n")
        n += 1
        print(path.relative_to(ROOT))
print(f"Updated {n}")