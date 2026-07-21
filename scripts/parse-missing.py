#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import pathlib, re, collections
t = pathlib.Path("build_int1.txt").read_text(encoding="utf-8", errors="replace")
errs = [l for l in t.splitlines() if " error " in l]
missing = collections.Counter()
for l in errs:
    m = re.search(r"name '([^']+)'", l)
    if m:
        missing[m.group(1)] += 1
out = pathlib.Path("build_missing.txt")
lines = [f"{v}\t{k}" for k, v in missing.most_common(80)]
out.write_text("\n".join(lines), encoding="utf-8")
print("wrote", out, "unique", len(missing), "errors", len(errs))
