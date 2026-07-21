#!/usr/bin/env python3
from pathlib import Path
root = Path("src/NurMarketKassa.Avalonia/Views.axaml")
out = Path("views_list.txt")
lines = []
lines.append(f"exists={root.exists()} is_dir={root.is_dir()}")
if root.exists():
    for p in sorted(root.rglob("*")):
        if p.is_file():
            lines.append(str(p.relative_to(root)))
out.write_text("\n".join(lines), encoding="utf-8")
print("wrote", len(lines), "lines")
