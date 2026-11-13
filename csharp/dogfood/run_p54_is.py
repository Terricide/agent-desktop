"""P54 dogfood: the C# CLI's `is` answers the same as the Rust CLI's, control by control,
on the same window, with refs captured independently by each build.

The operator drives only the shipped command surface: launch, list-windows, focus-window,
snapshot, is, and close only the pid the run launched.
"""

import json
import os
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CS = os.path.join(ROOT, "src", "AgentDesktop.Cli", "bin", "Debug", "net8.0-windows", "agent-desktop.exe")
RS = os.path.join(ROOT, "..", "target", "debug", "agent-desktop.exe")
MARKER = "p54parity" + str(int(time.time()))
PROPERTIES = ["focused", "checked", "enabled", "expanded", "selected", "visible"]


def run(exe, *args, session=None):
    env = dict(os.environ)
    if session:
        env["AGENT_DESKTOP_SESSION"] = session
    out = subprocess.run([exe] + list(args), capture_output=True, timeout=60, env=env).stdout.decode("utf-8", "replace")
    try:
        return json.loads(out)
    except ValueError:
        return {"ok": False, "code": "NO_JSON", "raw": out[:120]}


def walk(node):
    yield node
    for child in node.get("children") or []:
        yield from walk(child)


def refs_by_identity(env):
    found = {}
    for node in walk(env["data"]["tree"]):
        if node.get("ref_id"):
            found.setdefault((node.get("role"), node.get("name")), node["ref_id"])
    return found


def find_window(app, marker):
    for _ in range(40):
        for window in run(CS, "list-windows", "--app", app).get("data") or []:
            if marker in (window.get("title") or ""):
                return window["id"]
        time.sleep(0.5)
    return None


def answer(env):
    data = env.get("data") or {}
    error = env.get("error") or {}
    return (env.get("ok"), data.get("result"), data.get("applicable"), error.get("code"))


def main():
    notepad_file = os.path.join(ROOT, MARKER)
    with open(notepad_file, "w") as handle:
        handle.write("p54\n")
    launched = subprocess.Popen(["notepad", notepad_file])
    window = find_window("Notepad.exe", MARKER)
    if not window:
        print("FAIL: no window for the launched file")
        return 1
    run(CS, "focus-window", "--window-id", window)
    time.sleep(1)

    cs_refs = refs_by_identity(run(CS, "snapshot", "--window-id", window, "--session", "p54c"))
    rust_refs = refs_by_identity(run(RS, "snapshot", "--window-id", window, "--session", "p54r"))
    shared = sorted(set(cs_refs) & set(rust_refs))[:12]
    mismatches = []
    checks = 0
    for role, name in shared:
        for prop in PROPERTIES:
            cs = answer(run(CS, "is", cs_refs[(role, name)], "--property", prop, "--session", "p54c"))
            rust = answer(run(RS, "is", rust_refs[(role, name)], "--property", prop, "--session", "p54r"))
            checks += 1
            if cs != rust:
                mismatches.append((role, name, prop, cs, rust))
            print(f"{'OK  ' if cs == rust else 'DIFF'} {role} {name!r} {prop} c#={cs} rust={rust}")

    subprocess.run(["taskkill", "/PID", str(launched.pid), "/F", "/T"], capture_output=True)
    os.remove(notepad_file)
    print(f"\n{checks - len(mismatches)}/{checks} agree with the Rust build")
    for row in mismatches:
        print("MISMATCH", row)
    return 1 if mismatches else 0


if __name__ == "__main__":
    sys.exit(main())
