#!/usr/bin/env python3
from pathlib import Path
import re, collections
t = Path("build_int2.txt").read_text(encoding="utf-8", errors="replace")
errs = [l for l in t.splitlines() if " error " in l]
miss = collections.Counter()
for l in errs:
    m = re.search(r"имя пространства имен \"([^\"]+)\"|тип или имя пространства имен \"([^\"]+)\"|name '([^']+)'", l)
    # Russian: Не удалось найти тип или имя пространства имен "X"
    m = re.search(r'пространства имен "([^"]+)"', l) or re.search(r"namespace name '([^']+)'", l)
    if not m:
        m = re.search(r'"([^"]+)"', l)
    if m:
        miss[m.group(1)] += 1
Path("build_miss2.txt").write_text("\n".join(f"{v}\t{k}" for k,v in miss.most_common(40)), encoding="utf-8")
print(miss.most_common(30))
