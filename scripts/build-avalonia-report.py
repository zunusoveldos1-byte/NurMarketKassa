#!/usr/bin/env python3
from pathlib import Path
import re, collections, subprocess, sys

r = subprocess.run(
    ["dotnet", "build", r"src\NurMarketKassa.Avalonia\NurMarketKassa.Avalonia.csproj", "-v", "q"],
    capture_output=True,
    text=True,
    encoding="utf-8",
    errors="replace",
)
Path("build_int2.txt").write_text(r.stdout + "\n" + r.stderr, encoding="utf-8")
t = Path("build_int2.txt").read_text(encoding="utf-8", errors="replace")
errs = [l for l in t.splitlines() if " error " in l]
print("EXIT", r.returncode, "ERR", len(errs))
c = collections.Counter()
for l in errs:
    m = re.search(r"error (CS\d+|AVLN\d+)", l)
    if m:
        c[m.group(1)] += 1
print(c.most_common(15))
miss = collections.Counter()
for l in errs:
    for m in re.finditer(r"'([^']{2,80})'", l):
        miss[m.group(1)] += 1
lines = [f"{v}\t{k}" for k, v in miss.most_common(60)]
lines.append("")
lines.extend(errs[:50])
Path("build_int2_summary.txt").write_text("\n".join(lines), encoding="utf-8")
print("summary written")
sys.exit(r.returncode)
