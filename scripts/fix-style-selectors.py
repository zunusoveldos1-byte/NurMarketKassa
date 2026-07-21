#!/usr/bin/env python3
"""Fix corrupted Avalonia Style selectors: Selector="Button"foo" -> Selector="Button.foo" """
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "src" / "NurMarketKassa.Avalonia"
# Pattern: Selector="Type"rest..."  where rest starts with letter/digit (class name)
# Examples:
#   Selector="Button"pos-dialog-primary"
#   Selector="Button"pos-dialog-primary:pointerover /template/ Border#bd"
#   Selector="RadioButton"DatePill"
#   Selector="DataGrid"ModernGrid" x:Key=...
PAT = re.compile(
    r'Selector="([^"]+)"([A-Za-z0-9_][A-Za-z0-9_.:#/\-\s]*)"'
)

changed_files = 0
total_repl = 0
for path in ROOT.rglob("*.axaml"):
    if not path.is_file():
        continue
    text = path.read_text(encoding="utf-8")
    new_text, n = PAT.subn(r'Selector="\1.\2"', text)
    if n:
        path.write_text(new_text, encoding="utf-8", newline="\n")
        changed_files += 1
        total_repl += n
        print(f"{path.relative_to(ROOT)}: {n}")

print(f"DONE files={changed_files} replacements={total_repl}")
