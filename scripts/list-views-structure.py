#!/usr/bin/env python3
from pathlib import Path

root = Path("src/NurMarketKassa.Avalonia/Views.axaml")
print("DIRS:")
for p in sorted([d for d in root.iterdir() if d.is_dir()], key=lambda x: x.name.lower()):
    print(" ", p.name)
print("ROOT FILES:")
for p in sorted([f for f in root.iterdir() if f.is_file()], key=lambda x: x.name.lower()):
    print(" ", p.name)
print("THIN:", len(list(root.rglob("*.Thin.cs"))))
print("Login/", list(Path(root/"Login").glob("*")) if (root/"Login").exists() else None)
print("Main/", [p.name for p in (root/"Main").iterdir()] if (root/"Main").exists() else None)
print("Controls/", [p.name for p in (root/"Controls").iterdir()] if (root/"Controls").exists() else None)
print("Shared/", [p.name for p in (root/"Shared").iterdir()] if (root/"Shared").exists() else None)
