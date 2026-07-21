#!/usr/bin/env python3
from pathlib import Path

root = Path(r"src/NurMarketKassa.Avalonia")
thins = [p for p in root.rglob("*.Thin.cs") if p.is_file()]
print(f"Found {len(thins)} Thin files")
for p in thins:
    print(f"  {p}")
    p.unlink()
print(f"Deleted {len(thins)}")

# Also delete orphan Thin at wrong locations under Views.axaml root that might remain
orphans = list(root.rglob("*.Thin.cs"))
print(f"Remaining: {len([p for p in orphans if p.is_file()])}")
