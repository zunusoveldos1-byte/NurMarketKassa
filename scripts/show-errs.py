#!/usr/bin/env python3
from pathlib import Path
import re, collections
t = Path("build_int2.txt").read_text(encoding="utf-8", errors="replace")
errs = [l for l in t.splitlines() if " error " in l]
miss = collections.Counter()
for l in errs:
    m = re.search(r'пространства имен "([^"]+)"', l)
    if not m:
        m = re.search(r'тип или имя пространства имен "([^"]+)"', l)
    if m:
        miss[m.group(1)] += 1
print("ERR", len(errs))
for k,v in miss.most_common(40):
    print(f"{v}\t{k}")
print("---")
for l in errs[:25]:
    # shorten path
    print(re.sub(r"C:\\Users\\[^:]+:", "", l)[:200])
