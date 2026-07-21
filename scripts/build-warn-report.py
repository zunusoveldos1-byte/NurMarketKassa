#!/usr/bin/env python3
from pathlib import Path
import re, collections, subprocess, sys

r = subprocess.run(
    ["dotnet", "build", r"src\NurMarketKassa.Avalonia\NurMarketKassa.Avalonia.csproj", "-v", "q", "-warnaserror:0"],
    capture_output=True, text=True, encoding="utf-8", errors="replace",
)
Path("build_warnfix.txt").write_text(r.stdout + "\n" + r.stderr, encoding="utf-8")
t = Path("build_warnfix.txt").read_text(encoding="utf-8", errors="replace")
errs = [l for l in t.splitlines() if " error " in l]
warns = [l for l in t.splitlines() if " warning " in l]
print("EXIT", r.returncode, "ERR", len(errs), "WARN", len(warns))
wc = collections.Counter()
for l in warns:
    m = re.search(r"warning (CA\d+|CS\d+|AVLN\d+)", l)
    if m:
        wc[m.group(1)] += 1
print("warn codes:", wc.most_common(20))
for l in errs[:20]:
    print("E:", l[:220])
for l in warns[:30]:
    print("W:", re.sub(r".*\\\\", "", l)[:220])
sys.exit(0 if r.returncode == 0 and len(errs) == 0 else 1)
