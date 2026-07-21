#!/usr/bin/env python3
from __future__ import annotations
import re
from pathlib import Path

ROOT = Path(r"C:\Users\User\Desktop\NurCrmPosKassa-master\NurCrmPosKassa-master\src\NurMarketKassa.Avalonia")

def strip_named_block(text: str, open_tag: str) -> str:
    pattern = re.compile(rf"<{open_tag}(\s[^>]*)?>", re.IGNORECASE)
    close = re.compile(rf"</{open_tag}\s*>", re.IGNORECASE)
    while True:
        m = pattern.search(text)
        if not m:
            break
        start = m.start()
        pos = m.end()
        depth = 1
        while depth > 0 and pos < len(text):
            nm = pattern.search(text, pos)
            cm = close.search(text, pos)
            if cm is None:
                text = text[:start] + text[m.end():]
                break
            if nm and nm.start() < cm.start():
                depth += 1
                pos = nm.end()
            else:
                depth -= 1
                pos = cm.end()
                if depth == 0:
                    text = text[:start] + text[pos:]
                    break
        else:
            break
    return text

def fix_axaml(text: str) -> str:
    text = re.sub(r'/\s*PasswordChar="[^"]*"\s*>', ' PasswordChar="\u2022">', text)
    for block in (
        "ControlTemplate.Triggers", "Style.Triggers", "DataTrigger", "Trigger",
        "MultiDataTrigger", "MultiTrigger", "EventTrigger", "BeginStoryboard",
        "Storyboard", "VisualStateManager.VisualStateGroups", "FrameworkTemplate.Resources",
    ):
        text = strip_named_block(text, block)
    text = re.sub(r"<Trigger(\s[^>/]*)?/>", "", text, flags=re.IGNORECASE)
    text = re.sub(r"<DataTrigger(\s[^>/]*)?/>", "", text, flags=re.IGNORECASE)
    for a in [
        r'\s+CanUserResizeRows="[^"]*"', r'\s+CanUserResizeColumns="[^"]*"',
        r'\s+SnapsToDevicePixels="[^"]*"', r'\s+UseLayoutRounding="[^"]*"',
        r'\s+FocusVisualStyle="[^"]*"', r'\s+TextOptions\.[A-Za-z]+="[^"]*"',
        r'\s+RenderOptions\.[A-Za-z]+="[^"]*"', r'\s+RecognizesAccessKey="[^"]*"',
        r'\s+VerticalContentAlignment="[^"]*"', r'\s+AllowsTransparency="[^"]*"',
        r'\s+WindowStyle="[^"]*"', r'\s+ResizeMode="[^"]*"',
        r'\s+OverridesDefaultStyle="[^"]*"', r'\s+KeyboardNavigation\.[A-Za-z]+="[^"]*"',
        r'\s+ScrollViewer\.[A-Za-z]+="[^"]*"', r'\s+VirtualizingPanel\.[A-Za-z]+="[^"]*"',
        r'\s+ContentSource="[^"]*"', r'\s+IsItemsHost="[^"]*"',
    ]:
        text = re.sub(a, "", text)
    text = text.replace("FrameworkElement.Resources", "Window.Resources")
    text = re.sub(r'Visibility="Collapsed"', 'IsVisible="False"', text)
    text = re.sub(r'Visibility="Hidden"', 'IsVisible="False"', text)
    text = re.sub(r'Visibility="Visible"', 'IsVisible="True"', text)
    text = re.sub(r"\n{3,}", "\n\n", text)
    return text

n = 0
for path in ROOT.rglob("*.axaml"):
    if not path.is_file():
        continue
    if "_wpf_port_backup" in path.parts or path.name.endswith(".bak"):
        continue
    original = path.read_text(encoding="utf-8", errors="replace")
    updated = fix_axaml(original)
    if updated != original:
        path.write_text(updated, encoding="utf-8", newline="\n")
        n += 1
        print(path.relative_to(ROOT))
print(f"Updated {n} axaml files")