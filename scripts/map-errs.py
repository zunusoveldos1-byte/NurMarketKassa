#!/usr/bin/env python3
from pathlib import Path
import re
t = Path("build_int2.txt").read_text(encoding="utf-8", errors="replace")
errs = [l for l in t.splitlines() if " error " in l]
for l in errs:
    file = l.split(":")[0]
    file = file.replace("\\", "/").split("/")[-1]
    miss = re.findall(r'"([^"]+)"', l)
    print(f"{file} -> {miss[:3]}")
