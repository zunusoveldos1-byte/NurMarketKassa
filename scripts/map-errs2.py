#!/usr/bin/env python3
from pathlib import Path
import re
t = Path("build_int2.txt").read_text(encoding="utf-8", errors="replace")
errs = [l for l in t.splitlines() if " error " in l]
for l in errs:
    m = re.search(r"([^\\/]+\.cs)\((\d+)", l)
    file = m.group(1) if m else "?"
    miss = re.findall(r'"([^"]+)"', l)
    print(f"{file}:{m.group(2) if m else '?'} -> {miss[:3]}")

print("\nCOMPAT lines:")
for l in t.splitlines():
    if "Compat" in l and ("error" in l or "warning CS" in l):
        print(l[:220])
