import collections, sys, os

log = sys.argv[1] if len(sys.argv) > 1 else "build.log"
lines = [l for l in open(log, encoding="utf-8", errors="replace") if ": error CS" in l]
by = collections.Counter()
uniq = set()
for l in lines:
    f = os.path.basename(l.split("(")[0])
    c = l.split("error CS")[1][:4]
    m = l.split("error CS")[1].split("[")[0][5:].strip()
    by[f] += 1
    uniq.add((f, c, m))

print("lines", len(lines), "unique", len(uniq))
for f, n in by.most_common():
    print("  %3d %s" % (n, f))

if len(sys.argv) > 2:
    out = open(sys.argv[2], "w", encoding="utf-8")
    byfile = {}
    for f, c, m in sorted(uniq):
        byfile.setdefault(f, []).append("   %s: %s" % (c, m))
    for f in sorted(byfile, key=lambda k: -len(byfile[k])):
        out.write(f + "\n")
        for e in byfile[f]:
            out.write(e + "\n")
    out.close()
