"""Multi-window, multi-mode parity sweep: Rust vs C#.

Runs both binaries over the live top-level windows and reports, per window and mode,
the structural differences: node count, ref count, ref ids, fields, truncation.
Dumps the JSON pair for the first differing window so a diff can be inspected offline.
"""

import json
import subprocess
import sys
import io

RUST = r"C:/Users/mreed/.t3/worktrees/agent-desktop/t3-11789549/target/debug/agent-desktop.exe"
CSHARP = r"C:/Users/mreed/.t3/worktrees/agent-desktop/t3-11789549/csharp/src/AgentDesktop.Cli/bin/Debug/net8.0/agent-desktop.exe"
out = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")


def run(exe, args, app=None):
    if app and exe == CSHARP:
        args = args + ["--app", app]
    p = subprocess.run([exe] + args, capture_output=True, timeout=120)
    return json.loads(p.stdout.decode("utf-8", errors="replace"))


def flat(d):
    rows = []

    def walk(n, path):
        rows.append((
            tuple(path),
            n.get("role"),
            (n.get("ref_id") or "").split(":")[-1],
            n.get("name"),
            n.get("description"),
            n.get("children_count"),
            len(n.get("children", [])),
            tuple(n.get("states") or []),
            tuple(n.get("actions") or []),
            n.get("subtree_truncated"),
        ))
        for i, c in enumerate(n.get("children", [])):
            walk(c, path + [i])

    walk(d["data"]["tree"], [])
    return rows


def main():
    windows = run(RUST, ["list-windows"])["data"]
    targets = [w["id"] for w in windows
               if w.get("title") and w.get("bounds", {}).get("height", 0) > 200 and not w.get("minimized")]
    modes = [[], ["--compact"], ["--interactive-only"], ["--skeleton"]]

    for wid in targets[:10]:
        for extra in modes:
            r = run(RUST, ["snapshot", "--window-id", wid] + extra)
            if not r["ok"]:
                out.write("window %s %s rust failed %s\n" % (wid, " ".join(extra) or "default", json.dumps(r.get("error", {}))))
                continue
            app = r["data"]["app"]
            c = run(CSHARP, ["snapshot", "--window-id", wid] + extra, app)
            if not c["ok"]:
                out.write("window %s %s csharp failed %s\n" % (wid, " ".join(extra) or "default", json.dumps(c.get("error", {}))))
                continue

            rf, cf = flat(r), flat(c)
            diffs = [i for i in range(min(len(rf), len(cf))) if rf[i] != cf[i]]
            shape = []
            if len(rf) != len(cf):
                shape.append("node-count")
            if r["data"].get("ref_count") != c["data"].get("ref_count"):
                shape.append("ref-count")
            if r["data"].get("complete") != c["data"].get("complete"):
                shape.append("complete")
            tag = " ".join(extra) or "default"
            out.write("window %s %s app=%s nodes R=%d C=%d refs R=%s C=%s diffs=%d %s\n"
                      % (wid, tag, app, len(rf), len(cf),
                         r["data"].get("ref_count"), c["data"].get("ref_count"),
                         len(diffs), " ".join(shape)))
            for i in diffs[:3]:
                out.write("  R %r\n  C %r\n" % (rf[i], cf[i]))
            if diffs and tag == "default":
                json.dump(r, open("p_r.json", "w", encoding="utf-8"), ensure_ascii=False)
                json.dump(c, open("p_c.json", "w", encoding="utf-8"), ensure_ascii=False)
            out.flush()

    out.flush()


main()
