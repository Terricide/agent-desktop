import json, subprocess, sys, time, os

RUST = r"C:/Users/mreed/.t3/worktrees/agent-desktop/t3-11789549/target/debug/agent-desktop.exe"
CSHARP = r"C:/Users/mreed/.t3/worktrees/agent-desktop/t3-11789549/csharp/src/AgentDesktop.Cli/bin/Debug/net8.0-windows/agent-desktop.exe"

def run(exe, args):
    p = subprocess.run([exe] + args, capture_output=True)
    return json.loads(p.stdout.decode("cp1252", errors="replace"))

def strip_ids(d):
    t = json.loads(json.dumps(d))
    def walk(n):
        if isinstance(n, dict):
            n.pop("snapshot_id", None)
            n.pop("source_window_id", None)
            for k, v in list(n.items()):
                if isinstance(v, str) and v.startswith("@"):
                    n[k] = "@" + v.split(":")[-1]
                walk(v)
        elif isinstance(n, list):
            for v in n:
                walk(v)
    walk(t)
    return t

def keys(d, acc):
    tree = d.get("data", {}).get("tree")
    def walk(n):
        for k in n:
            acc[k] += 1
            for c in n.get("children", []):
                walk(c)
    if tree:
        walk(tree)
    return acc

def norm_ref(ref):
    return ref.split(":")[-1] if ref else ref

def flat(d):
    tree = d["data"]["tree"]
    rows = []
    def walk(n, path):
        rows.append((
            path,
            n.get("role"),
            norm_ref(n.get("ref_id")),
            n.get("name"),
            n.get("description"),
            n.get("children_count"),
            len(n.get("children", [])),
            n.get("states"),
            n.get("available_actions"),
        ))
        for i, c in enumerate(n.get("children", [])):
            walk(c, path + [i])
    walk(tree, [])
    return rows

def main():
    app = sys.argv[1] if len(sys.argv) > 1 else "notepad"
    extra = sys.argv[2:]
    subprocess.run(["cmd", "/c", "start", app], capture_output=True)
    time.sleep(2.5)

    windows = run(RUST, ["list-windows", "--app", app])
    targets = [w for w in windows["data"] if w.get("title")]
    if not targets:
        print("no window for", app)
        return 1
    wid = targets[0]["id"]
    print("window", wid)

    r = strip_ids(run(RUST, ["snapshot", "--window-id", wid] + extra))
    c = strip_ids(run(CSHARP, ["snapshot", "--window-id", wid] + extra))

    print("rust ok", r["ok"], "csharp ok", c["ok"])
    rk, ck = keys(r, {}), keys(c, {})
    print("rust-only keys", sorted(set(rk) - set(ck)))
    print("csharp-only keys", sorted(set(ck) - set(rk)))
    print("rust ref_count", r["data"].get("ref_count"), "csharp ref_count", c["data"].get("ref_count"))

    rf, cf = flat(r), flat(c)
    print("rust nodes", len(rf), "csharp nodes", len(cf))
    for i in range(min(len(rf), len(cf))):
        if rf[i] != cf[i]:
            print("DIFF at", rf[i][0])
            print("  rust  ", rf[i])
            print("  csharp", cf[i])
    subprocess.run(["taskkill", "/f", "/im", app + ".exe"], capture_output=True)
    return 0

sys.exit(main())
