"""Architecture boundaries for the agent-desktop workspace.

Fails when platform code leaks into core, when one platform crate reaches
another, when a crate's top-level layout drifts, or when a file sits in the
wrong layer. Manifests are read through `cargo metadata --no-deps --offline`,
so renames, quoted keys, inherited workspace dependencies and build scripts
are seen exactly as Cargo sees them. Sources are read through a small lexer
that turns comments into whitespace and every string literal into a
placeholder carrying its decoded value, so a path written inside a string is
never mistaken for code and an escape sequence cannot hide a path.

Usage: check_architecture.py <repo-root> [--skip-self-test]
"""
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

PLATFORMS = ("macos", "windows", "linux")
PLATFORM_PACKAGES = {f"agent-desktop-{p}" for p in PLATFORMS}
WIRING_PACKAGES = {"agent-desktop", "agent-desktop-ffi"}
TOP_PACKAGES = {"agent-desktop", "agent-desktop-ffi"}
CORE_PACKAGE = "agent-desktop-core"
CORE_DEPENDENCIES = {"serde", "serde_json", "thiserror", "tracing", "rustc-hash", "base64", "libc", "smallvec"}
PLATFORM_LAYERS = {"tree", "actions", "input", "system", "notifications"}
PLATFORM_FILES = {"lib.rs", "adapter.rs"}
PLATFORM_OPTIONAL = {"adapter_tests.rs", "bin"}
CORE_TOP_FILES = {"lib.rs"}
CORE_DOMAINS = {"actionability", "actions", "adapter", "commands", "context", "cursor_overlay", "input",
                "locator", "notifications", "ref_action", "refs", "response", "session", "snapshot",
                "storage", "system", "trace", "tree", "wait"}
BINARY_TOP_FILES = {"main.rs", "Cargo.toml"}
BINARY_DIRS = {"batch", "cli", "cli_args", "command_policy", "diagnostic", "dispatch", "mcp",
               "tests", "visual_debug"}
CAPABILITY_HOMES = {
    "PlatformAdapter": ("adapter.rs",),
    "ObservationOps": ("adapter.rs", "tree/adapter.rs"),
    "ActionOps": ("adapter.rs", "actions/adapter.rs"),
    "InputOps": ("adapter.rs", "input/adapter.rs"),
    "SystemOps": ("adapter.rs", "system/adapter.rs"),
}
# Core files allowed to carry OS-conditional code. Every entry must still match,
# so the list only shrinks on purpose. Paths are relative to crates/core/src.
CORE_OS_ALLOWLIST = {
    "commands/clipboard_get_tests.rs",
    "commands/clipboard_set_tests.rs",
    "commands/screenshot_tests.rs",
    "commands/session.rs",
    "commands/skills_tests.rs",
    "commands/skills.rs",
    "context/context_tests.rs",
    "cursor_overlay/image_tests.rs",
    "cursor_overlay/image.rs",
    "lib.rs",
    "refs/mod.rs",
    "refs/refs_store_tests.rs",
    "refs/refs_tests.rs",
    "session/interaction_lease_tests.rs",
    "session/interaction_lease.rs",
    "session/session_gc_tests.rs",
    "session/session_tests.rs",
    "session/tree_tests.rs",
    "session/tree.rs",
    "storage/file_lock.rs",
    "storage/private_file_parent.rs",
    "storage/private_file_tests.rs",
    "storage/private_file.rs",
    "storage/state_root.rs",
    "trace/trace_artifact_budget_tests.rs",
    "trace/trace_artifacts_more_tests.rs",
    "trace/trace_artifacts_tests.rs",
    "trace/trace_artifacts_toctou_tests.rs",
    "trace/trace_file.rs",
    "trace/trace_read/html_screenshot_tests.rs",
    "trace/trace_read/html_tests.rs",
    "trace/trace_read/html_write_tests.rs",
    "trace/trace_read/input_tests.rs",
    "trace/trace_read/mod_tests.rs",
    "trace/trace_read/segment_tests.rs",
    "trace/trace_tests.rs",
}

# Every CLI call is its own process that serves nothing. Only these files may
# open a listener or server, and only for the reason beside them; a new entry
# is a review decision. Test files and inline test modules are not scanned.
LISTENER_ALLOWLIST = {
    "crates/core/src/system/cdp_endpoint.rs",
    "crates/macos/src/system/cursor_overlay/child.rs",
    "crates/windows/src/system/cursor_overlay/server.rs",
}
LISTENER = re.compile(r"(?<![A-Za-z0-9_])(?:(?:UnixListener|TcpListener|UdpSocket|UnixDatagram)\s*::\s*bind\w*"
                      r"|CreateNamedPipe[AW]?|NamedPipeServer|ServerOptions|libc\s*::\s*listen)(?![A-Za-z0-9_])")
HOST_MARKER = "AGENT_DESKTOP_INTERNAL_COMMAND_HOST"
HOST_MARKER_ALLOWLIST = {"crates/macos/src/system/process/legacy_host.rs"}
TEST_FILE = re.compile(r"(^|/)tests/|_tests\.rs$|test_support")
TEST_MODULE = re.compile(r"#\s*\[\s*cfg\s*\(\s*test\s*\)\s*\]\s*(?:pub(?:\s*\([^)]*\))?\s+)?mod\s+\w+\s*\{")


STR = re.compile(r"__S(\d+)__")

# --- C# side ---------------------------------------------------------------
# The same boundaries for the csharp/ tree. The .csproj graph stands in for the Cargo
# manifest graph, `using` directives and namespace literals for crate paths, and a
# `partial class` declaration for the inline test module Rust declares with
# `#[cfg(test)] #[path = "..."] mod tests;`. G11 has no C# analogue: C# has no
# `#[path]` attribute, so a file cannot declare a source outside its folder.
CS_PLATFORMS = ("windows", "macos", "linux")
CS_PROJECT_SUFFIX = tuple(sorted(p.capitalize() for p in CS_PLATFORMS))
CS_PLATFORM_PACKAGES = {f"AgentDesktop.{p}" for p in CS_PROJECT_SUFFIX}
CS_WIRING_PACKAGES = {"AgentDesktop.Cli", "AgentDesktop.Ffi"}
CS_CORE_PACKAGE = "AgentDesktop.Core"
CS_PLATFORM_LAYERS = {"Tree", "Actions", "Input", "System", "Notifications"}
CS_PLATFORM_FILES = {"Adapter.cs"}
CS_PLATFORM_OPTIONAL = {"Uia"}
CS_CORE_TOP_FILES = {"AgentDesktop.Core.csproj"}
# A build-time analyzer is not a runtime dependency, so core may reference exactly this
# one, and only with the marker that proves the assembly is never loaded into core.
CS_ANALYZER_PACKAGE = "AgentDesktop.SourceGen"
CS_CORE_DOMAINS = {"Actionability", "Actions", "Adapter", "Commands", "Context", "CursorOverlay",
                   "Input", "Locator", "Notifications", "Refs", "Response", "Session", "Snapshot",
                   "Storage", "Systems", "Trace", "Tree", "Wait"}
CS_CAPABILITY_HOMES = {
    "PlatformAdapter": ("Adapter.cs",),
    "ObservationOps": ("Adapter.cs", "Tree/Adapter.cs"),
    "ActionOps": ("Adapter.cs", "Actions/Adapter.cs"),
    "InputOps": ("Adapter.cs", "Input/Adapter.cs"),
    "SystemOps": ("Adapter.cs", "System/Adapter.cs"),
}
# Core files allowed to carry OS-conditional code. Every entry must still match, so the
# list only shrinks on purpose. Paths are relative to csharp/src/AgentDesktop.Core.
CS_OS_ALLOWLIST = set()
CS_LISTENER_ALLOWLIST = set()
CS_HOST_MARKER_ALLOWLIST = set()
CS_TEST_FILE = re.compile(r"(^|/)tests/|Tests\.cs$|TestSupport")
CS_TEST_MODULE = re.compile(r"\bpartial\s+class\s+([A-Za-z_][A-Za-z0-9_]*)")
CS_LISTENER = re.compile(r"(?<![A-Za-z0-9_])(?:TcpListener|NamedPipeServerStream|UdpSocket|UnixDomainSocket)"
                         r"(?:\s*\.\s*\w+|\s*\(|\s+[\w(])")
CS_HOST_MARKER = "AGENT_DESKTOP_INTERNAL_COMMAND_HOST"
CS_SPECIAL = re.compile(r"//|/\*|\"|'")
# The C# surface that is OS-conditional: a platform import, an OS guard attribute, a
# runtime OS probe, a Windows-only target framework, a Windows UI framework, or the
# Windows registry. Core must carry none of them.
OS_TOKENS = ("DllImport(\"user32", "RuntimeInformation.IsOSPlatform", "OSPlatform.Windows",
             "NamespaceInteropServices", "SupportedOSPlatform", "OperatingSystem", "IsWindows",
             "IsMacOS", "IsLinux", "net8.0-windows", "UseWindowsForms", "System.Windows",
             "System.Threading", "Microsoft.Win32", "Registry")
OS_GUARD = re.compile(r"(?<![A-Za-z0-9_])(?:SupportedOSPlatform|DllImport|RuntimeInformation|"
                      r"OperatingSystem|NamespaceInteropServices)(?![A-Za-z0-9_])")


class Source:
    """A Rust file as code (comments blanked, literals as __S<n>__) plus decoded literals."""

    def __init__(self, path):
        self.path = path
        with open(path, encoding="utf-8") as fh:
            self.code, self.lits = lex(fh.read())

    def lit(self, token):
        m = STR.fullmatch(token.strip())
        return self.lits[int(m.group(1))] if m else None


def decode(body):
    out, i = [], 0
    simple = {"n": "\n", "r": "\r", "t": "\t", "\\": "\\", "0": "\0", "'": "'", '"': '"'}
    while i < len(body):
        c = body[i]
        if c != "\\":
            out.append(c)
            i += 1
            continue
        nxt = body[i + 1] if i + 1 < len(body) else ""
        if nxt in simple:
            out.append(simple[nxt])
            i += 2
        elif nxt == "x":
            out.append(chr(int(body[i + 2:i + 4], 16)))
            i += 4
        elif nxt == "u":
            end = body.index("}", i)
            out.append(chr(int(body[i + 3:end].replace("_", ""), 16)))
            i = end + 1
        elif nxt == "\n":
            i += 2
            while i < len(body) and body[i] in " \t\r\n":
                i += 1
        else:
            out.append(nxt)
            i += 2
    return "".join(out)


SPECIAL = re.compile(r"//|/\*|\"|'")


def lex(src):
    """Return (code, literals): comments become spaces (newlines kept), strings become __S<n>__."""
    out, lits, i, n = [], [], 0, len(src)
    while i < n:
        m = SPECIAL.search(src, i)
        if m is None:
            out.append(src[i:])
            break
        s = m.start()
        prefix = re.search(r"(?<![A-Za-z0-9_])(?:[bc]?r#*|[bc])$", src[max(i, s - 70):s]) if src[s] in "\"'" else None
        if prefix and src[s] == "'" and prefix.group(0) != "b":
            prefix = None
        start_tok = s - len(prefix.group(0)) if prefix else s
        out.append(src[i:start_tok])
        tok = src[start_tok:s + 1]
        if tok == "/" and src.startswith("//", s):
            j = src.find("\n", s)
            j = n if j < 0 else j
            out.append(" " * (j - s))
            i = j
            continue
        if src.startswith("/*", s):
            depth, j = 1, s + 2
            while j < n and depth:
                nxt = src.find("/*", j)
                cls = src.find("*/", j)
                if cls < 0:
                    j = n
                    break
                if 0 <= nxt < cls:
                    depth, j = depth + 1, nxt + 2
                else:
                    depth, j = depth - 1, cls + 2
            out.append("".join(ch if ch == "\n" else " " for ch in src[s:j]))
            i = j
            continue
        if src[s] == '"':
            raw = re.fullmatch(r"[bc]?r(#*)", prefix.group(0)) if prefix else None
            if raw:
                close = '"' + raw.group(1)
                j = src.find(close, s + 1)
                j = n if j < 0 else j
                lits.append(src[s + 1:j])
                i = j + len(close)
            else:
                j = s + 1
                while j < n and src[j] != '"':
                    j += 2 if src[j] == "\\" else 1
                lits.append(decode(src[s + 1:j]))
                i = j + 1
            out.append(f"__S{len(lits) - 1}__")
            continue
        if src[s] == "'":
            ch = re.match(r"(?:\\(?:x[0-9a-fA-F]{2}|u\{[0-9a-fA-F_]+\}|.)|[^\\'\n])'", src[s + 1:s + 17])
            if ch:
                out.append("' '")
                i = s + 1 + ch.end()
                continue
            out.append("'")
            i = s + 1
            continue
        out.append(src[s])
        i = s + 1
    return "".join(out), lits


def rs_files(root):
    for base, dirs, files in os.walk(root):
        dirs[:] = sorted(d for d in dirs if d not in ("target", ".git", "node_modules"))
        for f in sorted(files):
            if f.endswith(".rs"):
                yield os.path.join(base, f)


def cs_files(root):
    """Every C# source under a root. `bin` and `obj` are build output, not source."""
    for base, dirs, files in os.walk(root):
        dirs[:] = sorted(d for d in dirs if d not in ("bin", "obj", ".git", "node_modules"))
        for f in sorted(files):
            if f.endswith(".cs"):
                yield os.path.join(base, f)


def cs_decode(body):
    out, i = [], 0
    simple = {"n": "\n", "r": "\r", "t": "\t", "\\": "\\", "0": "\0", "'": "'", '"': '"', "a": "\a", "f": "\f", "v": "\v"}
    while i < len(body):
        c = body[i]
        if c != "\\":
            out.append(c)
            i += 1
            continue
        nxt = body[i + 1] if i + 1 < len(body) else ""
        if nxt in simple:
            out.append(simple[nxt])
            i += 2
        elif nxt == "x":
            end = i + 2
            while end < len(body) and body[end] in "0123456789abcdefABCDEF":
                end += 1
            out.append(chr(int(body[i + 2:end], 16)) if end > i + 2 else "")
            i = end
        elif nxt == "u":
            out.append(chr(int(body[i + 2:i + 6], 16)))
            i += 6
        else:
            out.append(nxt)
            i += 2
    return "".join(out)


def cs_lex(src):
    """Return (code, literals): comments become spaces (newlines kept), every string
    literal - regular, verbatim, interpolated, raw - becomes a __S<n>__ placeholder."""
    out, lits, i, n = [], [], 0, len(src)
    while i < n:
        m = CS_SPECIAL.search(src, i)
        if m is None:
            out.append(src[i:])
            break
        s = m.start()
        out.append(src[i:s])
        if src.startswith("//", s):
            j = src.find("\n", s)
            j = n if j < 0 else j
            out.append(" " * (j - s))
            i = j
            continue
        if src.startswith("/*", s):
            depth, j = 1, s + 2
            while j < n and depth:
                nxt = src.find("/*", j)
                cls = src.find("*/", j)
                if cls < 0:
                    j = n
                    break
                if 0 <= nxt < cls:
                    depth, j = depth + 1, nxt + 2
                else:
                    depth, j = depth - 1, cls + 2
            out.append("".join(ch if ch == "\n" else " " for ch in src[s:j]))
            i = j
            continue
        if src[s] == '"':
            head = src[max(0, s - 6):s]
            if src.startswith('"""', s):
                j = src.find('"""', s + 3)
                j = n if j < 0 else j
                lits.append(src[s + 3:j])
                i = j + 3
                out.append(f"__S{len(lits) - 1}__")
                continue
            verbatim = head.endswith("@")
            j = s + 1
            while j < n:
                if src[j] == '"':
                    if verbatim and j + 1 < n and src[j + 1] == '"':
                        j += 2
                        continue
                    break
                j += 1 if verbatim else (2 if src[j] == "\\" else 1)
            body = src[s + 1:j]
            lits.append(body.replace('""', '"') if verbatim else cs_decode(body))
            i = j + 1
            out.append(f"__S{len(lits) - 1}__")
            continue
        if src[s] == "'":
            ch = re.match(r"(?:\\.|[^\\'\n])'", src[s + 1:s + 6])
            if ch:
                out.append("' '")
                i = s + 1 + ch.end()
                continue
            out.append("'")
            i = s + 1
            continue
        out.append(src[s])
        i = s + 1
    return "".join(out), lits


class CsSource:
    """A C# file as code (comments blanked, literals as __S<n>__) plus decoded literals."""

    def __init__(self, path):
        self.path = path
        with open(path, encoding="utf-8") as fh:
            self.code, self.lits = cs_lex(fh.read())

    def lit(self, token):
        m = STR.fullmatch(token.strip())
        return self.lits[int(m.group(1))] if m else None


def has_files(path):
    """Directories that hold no file are leftovers git does not track."""
    return any(files for _, _, files in os.walk(path))


def balanced(code, i):
    """Index after the bracket group opening at code[i]."""
    pairs = {"(": ")", "[": "]", "{": "}"}
    stack = [pairs[code[i]]]
    j = i + 1
    while j < len(code) and stack:
        ch = code[j]
        if ch in pairs:
            stack.append(pairs[ch])
        elif ch == stack[-1]:
            stack.pop()
        j += 1
    return j


def includes(src):
    """Yield (macro, literal-or-None, offset) for include!/include_str!/include_bytes! in any delimiter."""
    for m in re.finditer(r"\b(include(?:_str|_bytes)?)\s*!\s*([\(\[\{])", src.code):
        end = balanced(src.code, m.end(2) - 1)
        inner = src.code[m.end(2):end - 1].strip().rstrip(",").strip()
        yield m.group(1), src.lit(inner), m.start()


def path_attrs(src):
    """Yield (literal-or-None, attribute_start, attribute_end) for `path = ...` in any outer attribute."""
    for m in re.finditer(r"#\s*\[", src.code):
        end = balanced(src.code, m.end() - 1)
        body = src.code[m.end():end - 1]
        for pm in re.finditer(r"(?<![A-Za-z0-9_])path\s*=\s*([^,\)\]]+)", body):
            yield src.lit(pm.group(1)), m.start(), end


def mod_decls(src):
    """Yield (name, attrs_text, path_literal) for every outline `mod name;` at any nesting."""
    for m in re.finditer(r"(?<![A-Za-z0-9_])mod\s+([A-Za-z_][A-Za-z0-9_]*)\s*;", src.code):
        before = src.code[:m.start()]
        cut = max(before.rfind(";"), before.rfind("}"), before.rfind("{"))
        head = before[cut + 1:]
        path_lit = None
        pm = re.search(r"(?<![A-Za-z0-9_])path\s*=\s*(__S\d+__)", head)
        if pm:
            path_lit = src.lit(pm.group(1))
        yield m.group(1), head, path_lit


def production_code(code):
    """The code with every inline `#[cfg(test)] mod name { ... }` block removed."""
    out, i = [], 0
    for m in TEST_MODULE.finditer(code):
        if m.start() < i:
            continue
        out.append(code[i:m.start()])
        i = balanced(code, m.end() - 1)
    out.append(code[i:])
    return "".join(out)


def cfg_test(attrs_text):
    return any(re.search(r"(?<![A-Za-z0-9_])test(?![A-Za-z0-9_])", c) for c in re.findall(r"cfg\s*\(([^\]]*)\)", attrs_text))


def cargo_packages(repo):
    cargo = shutil.which("cargo")
    if cargo is None:
        raise SystemExit("check_architecture: cargo is required to read the manifests (cargo metadata)")
    # The self-test rebuilds the same manifests dozens of times; re-running cargo
    # for each plant is the gate's whole cost, so the read is cached on the manifest
    # content, which is exactly what cargo sees.
    key = tuple((p, open(p, "rb").read()) for p in sorted(
        os.path.join(base, f) for base, dirs, files in os.walk(repo)
        for f in files if f.endswith(".toml")))
    if key in _cargo_cache:
        return _cargo_cache[key]
    out = subprocess.run([cargo, "metadata", "--format-version", "1", "--no-deps", "--offline",
                          "--manifest-path", os.path.join(repo, "Cargo.toml")],
                         capture_output=True, text=True)
    if out.returncode != 0:
        raise SystemExit("check_architecture: cargo metadata failed:\n" + out.stderr)
    packages = json.loads(out.stdout)["packages"]
    _cargo_cache[key] = packages
    return packages


_cargo_cache = {}


def cs_projects(repo):
    """Every .csproj under csharp/, keyed by its own real path, with the references and
    properties Cargo reads for the Rust side."""
    root = os.path.normpath(os.path.join(repo, "csharp"))
    projects = {}
    for base, dirs, files in os.walk(root):
        dirs[:] = sorted(d for d in dirs if d not in ("bin", "obj", ".git"))
        for f in files:
            if not f.endswith(".csproj"):
                continue
            path = os.path.join(base, f)
            with open(path, encoding="utf-8") as fh:
                text = fh.read()
            name = re.search(r"<AssemblyName>([^<]*)</AssemblyName>", text)
            projects[os.path.normpath(path)] = {
                "name": name.group(1) if name else os.path.basename(path)[:-7],
                "path": path,
                "folder": os.path.normpath(base),
                "refs": re.findall(r"<ProjectReference[^>]*Include=\"([^\"]+)\"([^>]*)/?>", text),
                "pkgs": re.findall(r"<PackageReference[^>]*Include=\"([^\"]+)\"", text),
                "tfm": re.findall(r"<TargetFramework>([^<]*)</TargetFramework>", text),
                "flags": re.findall(r"<(UseWindowsForms|UseWPF)>", text),
            }
    return projects


def cs_identity(name):
    if name == CS_CORE_PACKAGE:
        return "core"
    if name in CS_PLATFORM_PACKAGES:
        return "platform"
    if name in CS_WIRING_PACKAGES:
        return "wiring"
    return "other"


def check_csharp(repo):
    """The same boundaries for csharp/: project graph, source reach, OS-conditional code,
    layout, capability homes, test placement, and listeners."""
    errors = []

    def err(rule, msg):
        errors.append(f"{rule}: {msg}")

    src_root = os.path.normpath(os.path.join(repo, "csharp"))
    projects = cs_projects(repo)
    if not projects:
        return errors

    for folder, p in projects.items():
        rel = os.path.relpath(p["path"], repo).replace(os.sep, "/")
        ident = cs_identity(p["name"])
        for ref, tail in p["refs"]:
            target = os.path.normpath(os.path.join(p["folder"], ref.replace("\\", os.sep)))
            tp = projects.get(target)
            tname = tp["name"] if tp else os.path.basename(ref)
            analyzer = "ReferenceOutputAssembly=\"false\"" in tail
            if analyzer and tname == CS_ANALYZER_PACKAGE:
                continue
            if ident == "core":
                err("G1", f"{rel} references {tname}; core references no project")
            elif ident == "platform" and tname != CS_CORE_PACKAGE:
                err("G1", f"{rel} references {tname}; a platform project references only {CS_CORE_PACKAGE}")
            elif ident == "other" and tname in CS_PLATFORM_PACKAGES:
                if p["name"] == f"{tname}.Tests":
                    continue
                err("G1", f"{rel} references {tname}; only {sorted(CS_WIRING_PACKAGES)} wire platform projects")
        if ident == "core":
            if p["pkgs"]:
                err("G2", f"{rel} depends on {', '.join(sorted(p['pkgs']))}; core references nothing")
            for tfm in p["tfm"]:
                if re.search(r"-(windows|linux|osx|macos)", tfm):
                    err("G2", f"{rel} targets {tfm}; core targets a single portable TFM")
            for flag in p["flags"]:
                err("G2", f"{rel} sets {flag}; core carries no platform UI framework")
            for f in sorted(os.listdir(p["folder"])):
                if f.endswith(".props") or f.endswith(".targets"):
                    err("G2", f"{rel} has {f}, a build script; core has none")

    sources = {f: CsSource(f) for f in cs_files(src_root)}
    proj_dir = {p: os.path.normpath(os.path.join(src_root, "src", f"AgentDesktop.{p}"))
                for p in ("Core",) + CS_PROJECT_SUFFIX}

    for owner in ("Core",) + CS_PROJECT_SUFFIX:
        banned = sorted(set(CS_PROJECT_SUFFIX) - {owner})
        for f, src in sources.items():
            if not os.path.normpath(f).startswith(proj_dir[owner] + os.sep):
                continue
            rel = os.path.relpath(f, repo).replace(os.sep, "/")
            for b in banned:
                if re.search(rf"(?<![A-Za-z0-9_])AgentDesktop\.{b}(?![A-Za-z0-9_])", src.code):
                    err("G3", f"{rel} names AgentDesktop.{b}")
                for lit in src.lits:
                    if lit and lit.startswith("../") and lit.endswith(".cs"):
                        target = os.path.normpath(os.path.normpath(
                            os.path.join(os.path.dirname(f), lit.replace("\\", os.sep))))
                        if target.startswith(proj_dir[b] + os.sep):
                            err("G4", f"{rel} reads {lit}, inside the {b} project")

    core_dir = proj_dir["Core"]
    seen = set()
    for f, src in sources.items():
        if not os.path.normpath(f).startswith(core_dir + os.sep):
            continue
        rel = os.path.relpath(f, core_dir).replace(os.sep, "/")
        hits = [t for t in OS_TOKENS if t in src.code]
        if OS_GUARD.search(src.code):
            hits.append("OS guard attribute")
        if hits:
            if rel in CS_OS_ALLOWLIST:
                seen.add(rel)
            else:
                err("G5", f"csharp/src/AgentDesktop.Core/{rel} carries OS-conditional code "
                          f"({', '.join(sorted(set(hits)))}); keep it behind PlatformAdapter, "
                          "or add the file to CS_OS_ALLOWLIST in review")
    for stale in sorted(CS_OS_ALLOWLIST - seen):
        err("G5", f"CS_OS_ALLOWLIST lists csharp/src/AgentDesktop.Core/{stale}, which no longer "
                  "carries OS-conditional code")

    for p in CS_PROJECT_SUFFIX:
        top = proj_dir[p]
        if not os.path.isdir(top):
            continue
        entries = {e for e in os.listdir(top) if e not in ("bin", "obj")
                   and (os.path.isfile(os.path.join(top, e)) or has_files(os.path.join(top, e)))}
        allowed = CS_PLATFORM_LAYERS | CS_PLATFORM_FILES | CS_PLATFORM_OPTIONAL | {f"AgentDesktop.{p}.csproj"}
        for extra in sorted(entries - allowed):
            err("G6", f"csharp/src/AgentDesktop.{p}/{extra} is outside the shared layout "
                      f"{sorted(CS_PLATFORM_LAYERS | CS_PLATFORM_FILES)}")

    if os.path.isdir(core_dir):
        entries = {e for e in os.listdir(core_dir) if e not in ("bin", "obj")
                   and (os.path.isfile(os.path.join(core_dir, e)) or has_files(os.path.join(core_dir, e)))}
        for extra in sorted(entries - CS_CORE_TOP_FILES - CS_CORE_DOMAINS):
            err("G7", f"csharp/src/AgentDesktop.Core/{extra} is outside the shared layout "
                      f"{sorted(CS_CORE_DOMAINS)}")

    cap = "|".join(CS_CAPABILITY_HOMES)
    impl_re = re.compile(rf"\bclass\b[^{{;]*?:[^{{;]*?(?<![A-Za-z0-9_])({cap})(?![A-Za-z0-9_])")
    alias_re = re.compile(rf"(?<![A-Za-z0-9_])({cap})\s+as\s+(?!_(?![A-Za-z0-9_]))[A-Za-z_]")
    for p in CS_PROJECT_SUFFIX:
        top = proj_dir[p]
        for f, src in sources.items():
            if not os.path.normpath(f).startswith(top + os.sep):
                continue
            rel = os.path.relpath(f, top).replace(os.sep, "/")
            if alias_re.search(src.code):
                err("G8", f"csharp/src/AgentDesktop.{p}/{rel} renames a capability with `as`")
            for m in impl_re.finditer(src.code):
                if rel not in CS_CAPABILITY_HOMES[m.group(1)]:
                    err("G8", f"csharp/src/AgentDesktop.{p}/{rel} implements {m.group(1)}; it belongs in "
                              f"{' or '.join(CS_CAPABILITY_HOMES[m.group(1)])} "
                              "(test doubles go in a *_Tests.cs beside a partial class)")

    for f, src in sources.items():
        if not os.path.normpath(f).startswith(core_dir + os.sep):
            continue
        rel = os.path.relpath(f, core_dir).replace(os.sep, "/")
        if rel.startswith("Commands/") or rel.endswith("Tests.cs"):
            continue
        if re.search(r"(?<![A-Za-z0-9_])AgentDesktop\.Core\.Commands(?![A-Za-z0-9_])", src.code):
            err("G9", f"csharp/src/AgentDesktop.Core/{rel} reaches the Commands domain; core domains "
                      "must not depend on command code")

    for f, src in sources.items():
        base = os.path.basename(f)
        if not base.endswith("Tests.cs"):
            continue
        rel = os.path.relpath(f, repo).replace(os.sep, "/")
        if "tests" in rel.split("/")[:-1]:
            continue
        names = [m.group(1) for m in CS_TEST_MODULE.finditer(src.code)]
        if not names:
            err("G10", f"{rel} declares no partial class; it does not compile into the code it tests")
            continue
        for name in names:
            owner = os.path.join(os.path.dirname(f), name + ".cs")
            if not os.path.isfile(owner):
                err("G10", f"{rel} extends {name}, whose file is not beside it")

    serving = set()
    for f, src in sources.items():
        rel = os.path.relpath(f, repo).replace(os.sep, "/")
        if CS_TEST_FILE.search(rel):
            continue
        if CS_LISTENER.search(src.code):
            if rel in CS_LISTENER_ALLOWLIST:
                serving.add(rel)
            else:
                err("G12", f"{rel} opens a listener or server; every call is its own process and serves "
                           "nothing, so a new server needs a CS_LISTENER_ALLOWLIST entry in review")
        if rel not in CS_HOST_MARKER_ALLOWLIST and any(CS_HOST_MARKER in lit for lit in src.lits):
            err("G12", f"{rel} names {CS_HOST_MARKER}, the marker of the removed command host")
    for stale in sorted(CS_LISTENER_ALLOWLIST - serving):
        err("G12", f"CS_LISTENER_ALLOWLIST lists {stale}, which no longer opens a listener")
    return errors


def check(repo):
    errors = []

    def err(rule, msg):
        errors.append(f"{rule}: {msg}")

    for pkg in cargo_packages(repo):
        name = pkg["name"]
        for dep in pkg["dependencies"]:
            dname = dep["name"]
            if dname in PLATFORM_PACKAGES and name not in WIRING_PACKAGES:
                err("G1", f"{name} depends on {dname} (as `{dep.get('rename') or dname}`); only "
                          f"{sorted(WIRING_PACKAGES)} wire platform crates")
            if dname in TOP_PACKAGES:
                err("G1", f"{name} depends on {dname}")
            if name == CORE_PACKAGE:
                if dname not in CORE_DEPENDENCIES:
                    err("G2", f"core depends on {dname} (as `{dep.get('rename') or dname}`); allowed: {sorted(CORE_DEPENDENCIES)}")
                if dep.get("target"):
                    err("G2", f"core declares {dname} for target `{dep['target']}`; core carries no platform-specific dependency")
        if name == CORE_PACKAGE and any("custom-build" in t["kind"] for t in pkg["targets"]):
            err("G2", "core has a build script")

    sources = {}
    for root in (os.path.join(repo, "crates"), os.path.join(repo, "src")):
        for f in rs_files(root):
            sources[f] = Source(f)

    crate_dir = {p: os.path.normpath(os.path.join(repo, "crates", p)) for p in PLATFORMS + ("core",)}
    for owner in ("core",) + PLATFORMS:
        banned = PLATFORMS if owner == "core" else tuple(p for p in PLATFORMS if p != owner)
        for f, src in sources.items():
            if not os.path.normpath(f).startswith(crate_dir[owner] + os.sep):
                continue
            rel = os.path.relpath(f, repo)
            for b in banned:
                if re.search(rf"(?<![A-Za-z0-9_])agent_desktop_{b}(?![A-Za-z0-9_])", src.code):
                    err("G3", f"{rel} names agent_desktop_{b}")
            targets = [(m, lit) for m, lit, _ in includes(src)] + [("#[path]", lit) for lit, _, _ in path_attrs(src)]
            for macro, lit in targets:
                if lit is None:
                    err("G4", f"{rel} computes the path of {macro}; only a plain string literal is allowed")
                    continue
                target = os.path.normpath(os.path.join(os.path.dirname(f), lit))
                for b in banned:
                    if target.startswith(crate_dir[b] + os.sep):
                        err("G4", f"{rel} reads {lit}, inside the {b} crate")

    core_src = os.path.join(repo, "crates", "core", "src")
    seen = set()
    for f, src in sources.items():
        if not f.startswith(core_src + os.sep):
            continue
        rel = os.path.relpath(f, core_src).replace(os.sep, "/")
        hits = []
        if re.search(r"\bcfg(?:_attr)?\s*!?\s*\([^;{]*?(?<![A-Za-z0-9_])(target_os|target_family|target_vendor|target_env|windows|unix)(?![A-Za-z0-9_])", src.code):
            hits.append("platform cfg")
        if re.search(r"\b(?:std|core)\s*::\s*os(?![A-Za-z0-9_])", src.code) or \
                re.search(r"\b(?:std|core)\s*::\s*\{[^;]*?(?<![A-Za-z0-9_:])os\s*(?:::|,|\}|as\b)", src.code):
            hits.append("std::os")
        if re.search(r"(?<![A-Za-z0-9_])(libc|winapi|windows_sys)(?![A-Za-z0-9_])", src.code):
            hits.append("OS binding crate")
        if re.search(r"(?<![A-Za-z0-9_:])windows\s*::", src.code):
            hits.append("windows crate")
        for m in re.finditer(r"\bextern\s+(__S\d+__)", src.code):
            if src.lit(m.group(1)) != "Rust":
                hits.append("foreign ABI")
        if hits:
            if rel in CORE_OS_ALLOWLIST:
                seen.add(rel)
            else:
                err("G5", f"crates/core/src/{rel} carries OS-conditional code ({', '.join(sorted(set(hits)))}); keep it "
                          "behind PlatformAdapter, or add the file to CORE_OS_ALLOWLIST in review")
    for stale in sorted(CORE_OS_ALLOWLIST - seen):
        err("G5", f"CORE_OS_ALLOWLIST lists crates/core/src/{stale}, which no longer carries OS-conditional code")

    serving = set()
    for f, src in sources.items():
        rel = os.path.relpath(f, repo).replace(os.sep, "/")
        if TEST_FILE.search(rel):
            continue
        if LISTENER.search(production_code(src.code)):
            if rel in LISTENER_ALLOWLIST:
                serving.add(rel)
            else:
                err("G12", f"{rel} opens a listener or server; every call is its own process and serves nothing, "
                           "so a new server needs a LISTENER_ALLOWLIST entry in review")
        if rel not in HOST_MARKER_ALLOWLIST and any(HOST_MARKER in lit for lit in src.lits):
            err("G12", f"{rel} names {HOST_MARKER}, the marker of the removed command host")
    for stale in sorted(LISTENER_ALLOWLIST - serving):
        err("G12", f"LISTENER_ALLOWLIST lists {stale}, which no longer opens a listener")

    for p in PLATFORMS:
        if not os.path.exists(os.path.join(repo, "crates", p, "Cargo.toml")):
            continue
        top = os.path.join(repo, "crates", p, "src")
        if not os.path.isdir(top):
            err("G6", f"crates/{p}/src is missing")
            continue
        entries = {e for e in os.listdir(top) if os.path.isfile(os.path.join(top, e)) or has_files(os.path.join(top, e))}
        for extra in sorted(entries - PLATFORM_LAYERS - PLATFORM_FILES - PLATFORM_OPTIONAL):
            err("G6", f"crates/{p}/src/{extra} is outside the shared layout {sorted(PLATFORM_LAYERS | PLATFORM_FILES)}")
        for missing in sorted((PLATFORM_LAYERS | PLATFORM_FILES) - entries):
            err("G6", f"crates/{p}/src/{missing} is missing from the shared layout")
    for root, files_ok, dirs_ok, label in ((core_src, CORE_TOP_FILES, CORE_DOMAINS, "core domain"),
                                           (os.path.join(repo, "src"), BINARY_TOP_FILES, BINARY_DIRS, "binary folder")):
        if not os.path.isdir(root):
            err("G7", f"{os.path.relpath(root, repo)} is missing")
            continue
        rel_root = os.path.relpath(root, repo)
        for e in sorted(os.listdir(root)):
            full = os.path.join(root, e)
            if os.path.isfile(full) and e not in files_ok:
                err("G7", f"{rel_root}/{e} sits at the crate root; place it in a folder")
            if os.path.isdir(full) and has_files(full) and e not in dirs_ok:
                err("G7", f"{rel_root}/{e}/ is not a listed {label}")
        for e in sorted(files_ok | dirs_ok):
            full = os.path.join(root, e)
            if not (os.path.isfile(full) or (os.path.isdir(full) and has_files(full))):
                err("G7", f"{rel_root}/{e} is listed but missing")

    declared = {}
    for f, src in sources.items():
        here = os.path.dirname(f)
        base = os.path.basename(f)
        owned_dir = here if base in ("mod.rs", "lib.rs", "main.rs") else os.path.join(here, base[:-3])
        for name, attrs, path_lit in mod_decls(src):
            if path_lit is None:
                target = os.path.normpath(os.path.join(owned_dir, name + ".rs"))
            else:
                target = os.path.normpath(os.path.join(here, path_lit))
            declared[target] = (f, cfg_test(attrs))
        for macro, lit, _ in includes(src):
            if macro == "include" and lit:
                declared[os.path.normpath(os.path.join(here, lit))] = (f, True)

    cap = "|".join(CAPABILITY_HOMES)
    impl_re = re.compile(rf"\bimpl\b[^{{;]*?(?<![A-Za-z0-9_])({cap})\s+for\b")
    alias_re = re.compile(rf"(?<![A-Za-z0-9_])({cap})\s+as\s+(?!_(?![A-Za-z0-9_]))[A-Za-z_]")
    for p in PLATFORMS:
        top = os.path.join(repo, "crates", p, "src")
        for f, src in sources.items():
            if not f.startswith(top + os.sep):
                continue
            rel = os.path.relpath(f, top).replace(os.sep, "/")
            if alias_re.search(src.code):
                err("G8", f"crates/{p}/src/{rel} renames a capability trait with `as`")
            test_module = rel.endswith("_tests.rs") and declared.get(os.path.normpath(f), (None, False))[1]
            for m in impl_re.finditer(src.code):
                if rel not in CAPABILITY_HOMES[m.group(1)] and not test_module:
                    err("G8", f"crates/{p}/src/{rel} implements {m.group(1)}; it belongs in "
                              f"{' or '.join(CAPABILITY_HOMES[m.group(1)])} (test doubles go in a #[cfg(test)] *_tests.rs)")

    for f, src in sources.items():
        if not f.startswith(core_src + os.sep):
            continue
        rel = os.path.relpath(f, core_src).replace(os.sep, "/")
        if rel.startswith("commands/"):
            continue
        if rel.endswith("_tests.rs") and declared.get(os.path.normpath(f), (None, False))[1]:
            continue
        code = src.code
        if rel == "lib.rs":
            code = re.sub(r"(?m)^\s*(?:pub\s+)?mod\s+commands\s*;", "", code, count=1)
        if re.search(r"(?<![A-Za-z0-9_])commands(?![A-Za-z0-9_])", code):
            err("G9", f"crates/core/src/{rel} reaches the commands module; core domains must not depend on command code")

    for f, src in sources.items():
        here = os.path.normpath(os.path.dirname(f))
        rel = os.path.relpath(f, repo)
        for lit, _, _ in path_attrs(src):
            if lit is None:
                continue
            target = os.path.normpath(os.path.join(here, lit))
            inside = target.startswith(here + os.sep)
            in_tests = "tests" in os.path.relpath(target, os.path.normpath(repo)).split(os.sep)[:-1]
            if not (inside or in_tests):
                err("G11", f"{rel}: #[path = \"{lit}\"] leaves its folder; keep the file beside its declarer or under a tests/ folder")

    for f in sources:
        rel = os.path.relpath(f, repo).replace(os.sep, "/")
        if not rel.endswith("_tests.rs"):
            continue
        decl = declared.get(os.path.normpath(f))
        if decl is None:
            err("G10", f"{rel} is declared by no module; it does not compile")
            continue
        if "tests" in rel.split("/")[:-1]:
            continue
        declarer = decl[0]
        same = os.path.dirname(declarer) == os.path.dirname(f)
        owner = os.path.join(os.path.dirname(os.path.dirname(f)), os.path.basename(os.path.dirname(f)) + ".rs")
        if not (same or os.path.normpath(declarer) == os.path.normpath(owner)):
            err("G10", f"{rel} is declared from {os.path.relpath(declarer, repo)}; tests live beside the code they test")
    errors += check_csharp(repo)
    return errors


CLEAN = {
    "Cargo.toml": '[workspace]\nmembers = ["crates/core", "crates/macos", "crates/windows", "crates/linux", "crates/ffi", "src"]\n'
                  'resolver = "2"\n[workspace.dependencies]\nserde = "1"\nsmallvec = "1"\n'
                  'agent-desktop-core = { path = "crates/core" }\n',
    "crates/core/Cargo.toml": '[package]\nname = "agent-desktop-core"\nversion = "0.0.0"\nedition = "2021"\n'
                              '[dependencies]\nserde.workspace = true\nsmallvec.workspace = true\nlibc = "0.2"\n',
    "crates/core/src/lib.rs": "pub mod commands;\npub mod tree;\n",
    "crates/core/src/tree/mod.rs": "pub mod node;\n",
    "crates/core/src/tree/node.rs": "pub struct Node<'a>(&'a str);\nconst Q: char = '\"';\n"
                                    "const S: &str = include_str!(r\"node.rs\");\n"
                                    "/* outer /* nested */ still a comment: impl SystemOps for X */\n"
                                    "const T: &str = \"mod fake_tests; include_str!(\\\"../../../macos/src/lib.rs\\\") std::os::unix\";\n"
                                    "#[cfg(test)]\n#[path = \"node_tests.rs\"]\nmod tests;\n",
    "crates/core/src/tree/node_tests.rs": "#[test]\nfn t() {}\n",
    "crates/core/src/commands/mod.rs": "pub mod version;\n",
    "crates/core/src/commands/version.rs": "pub fn execute() {}\n",
    "crates/ffi/Cargo.toml": '[package]\nname = "agent-desktop-ffi"\nversion = "0.0.0"\nedition = "2021"\n'
                             '[target.\'cfg(target_os = "macos")\'.dependencies]\nagent-desktop-macos = { path = "../macos" }\n',
    "crates/ffi/src/lib.rs": "\n",
    "src/Cargo.toml": '[package]\nname = "agent-desktop"\nversion = "0.0.0"\nedition = "2021"\n[[bin]]\nname = "agent-desktop"\npath = "main.rs"\n'
                      '[target.\'cfg(target_os = "linux")\'.dependencies]\nagent-desktop-linux = { path = "../crates/linux" }\n',
    "src/main.rs": "mod batch;\nmod cli;\nmod cli_args;\nmod command_policy;\nmod diagnostic;\nmod dispatch;\n"
                   "mod visual_debug;\n#[cfg(test)]\n#[path = \"tests/main_tests.rs\"]\nmod main_tests;\nfn main() {}\n",
    "src/tests/main_tests.rs": "\n",
}
for _d in sorted(BINARY_DIRS - {"tests"}):
    CLEAN[f"src/{_d}/mod.rs"] = "\n"
for _d in sorted(CORE_DOMAINS - {"tree", "commands"}):
    CLEAN["crates/core/src/lib.rs"] += f"mod {_d};\n"
    CLEAN[f"crates/core/src/{_d}/mod.rs"] = "\n"
for _p in PLATFORMS:
    CLEAN[f"crates/{_p}/Cargo.toml"] = (f'[package]\nname = "agent-desktop-{_p}"\nversion = "0.0.0"\nedition = "2021"\n'
                                        "[dependencies]\nagent-desktop-core.workspace = true\n")
    CLEAN[f"crates/{_p}/src/lib.rs"] = "mod actions;\nmod adapter;\nmod input;\nmod notifications;\nmod system;\nmod tree;\n"
    CLEAN[f"crates/{_p}/src/adapter.rs"] = "pub struct A;\nimpl PlatformAdapter for A {}\n"
    for _layer, _trait in (("tree", "ObservationOps"), ("actions", "ActionOps"), ("input", "InputOps"), ("system", "SystemOps")):
        CLEAN[f"crates/{_p}/src/{_layer}/mod.rs"] = "mod adapter;\n"
        CLEAN[f"crates/{_p}/src/{_layer}/adapter.rs"] = f"use agent_desktop_core::{_trait} as _;\nimpl {_trait} for crate::adapter::A {{}}\n"
    CLEAN[f"crates/{_p}/src/notifications/mod.rs"] = "#[cfg(test)]\n#[path = \"double_tests.rs\"]\nmod double_tests;\n"
    CLEAN[f"crates/{_p}/src/notifications/double_tests.rs"] = "struct D;\nimpl SystemOps for D {}\n"

PLANTS = {
    "G1": [("crates/linux/Cargo.toml", CLEAN["crates/linux/Cargo.toml"] + 'agent-desktop-windows = { path = "../windows" }\n'),
           ("crates/linux/Cargo.toml", CLEAN["crates/linux/Cargo.toml"] + 'other = { package = "agent-desktop-macos", path = "../macos" }\n'),
           ("crates/core/Cargo.toml", CLEAN["crates/core/Cargo.toml"] + '[dev-dependencies]\nagent-desktop-macos = { path = "../macos" }\n')],
    "G2": [("crates/core/Cargo.toml", CLEAN["crates/core/Cargo.toml"] + '[target.x86_64-unknown-linux-gnu.dependencies]\nserde_json = "1"\n'),
           ("crates/core/Cargo.toml", CLEAN["crates/core/Cargo.toml"] + 'zbus = "5"\n'),
           ("crates/core/Cargo.toml", CLEAN["crates/core/Cargo.toml"] + '"windows-sys" = "0.61"\n'),
           ("crates/core/Cargo.toml", CLEAN["crates/core/Cargo.toml"] + 'base64 = { package = "windows-sys", version = "0.61" }\n'),
           ("Cargo.toml", CLEAN["Cargo.toml"].replace('smallvec = "1"', 'smallvec = { package = "x11rb", version = "0.14" }')),
           ("crates/core/Cargo.toml", CLEAN["crates/core/Cargo.toml"].replace("[package]\n", '[package]\nbuild = "custom.rs"\n')),
           ("crates/core/Cargo.toml", CLEAN["crates/core/Cargo.toml"] + '[dependencies.thiserror]\npackage = "zbus"\nversion = "5"\n')],
    "G3": [("crates/core/src/tree/leak.rs", "use agent_desktop_macos::MacOSAdapter;\n"),
           ("crates/linux/src/tree/leak.rs", "fn f() { let _ = agent_desktop_windows::X; }\n")],
    "G4": [("crates/core/src/tree/leak.rs", "const S: &str = include_str!(\"../../../macos/src/adapter.rs\");\n"),
           ("crates/core/src/tree/leak.rs", "const S: &str = include_str! { \"../../../macos/src/adapter.rs\" };\n"),
           ("crates/core/src/tree/leak.rs", "const S: &str = include_str!(\"../../../\\x6dacos/src/adapter.rs\");\n"),
           ("crates/core/src/tree/leak.rs", "const S: &str = include_str!(concat!(env!(\"CARGO_MANIFEST_DIR\"), \"/../macos/src/adapter.rs\"));\n"),
           ("crates/windows/src/tree/mod.rs", "#[path = \"../../../linux/src/adapter.rs\"]\nmod other;\n")],
    "G5": [("crates/core/src/tree/leak.rs", "#[cfg(target_os = \"windows\")]\nfn f() {}\n"),
           ("crates/core/src/tree/leak.rs", "use std::{fs::Permissions, os::unix::fs::PermissionsExt};\n"),
           ("crates/core/src/tree/leak.rs", "use std::os::{unix::fs::OpenOptionsExt};\n"),
           ("crates/core/src/tree/leak.rs", "use std::os as native;\n"),
           ("crates/core/src/tree/leak.rs", "use libc as native;\nfn f() { unsafe { native::getpid() }; }\n"),
           ("crates/core/src/tree/leak.rs", "unsafe extern \"system\" { fn GetTickCount() -> u32; }\n")],
    "G6": [("crates/macos/src/cf_type.rs", "pub struct CfType;\n"),
           ("crates/windows/src/sys/mod.rs", "\n"),
           ("crates/linux/src/adapter.rs", None)],
    "G7": [("crates/core/src/loose.rs", "pub struct Loose;\n"),
           ("crates/core/src/misc/mod.rs", "\n"),
           ("src/helper.rs", "\n"),
           ("crates/core/src/wait/mod.rs", None)],
    "G8": [("crates/linux/src/input/keyboard.rs", "impl SystemOps for crate::adapter::A {}\n"),
           ("crates/linux/src/system/adapter.rs", "use agent_desktop_core::SystemOps as LifecycleOps;\nimpl LifecycleOps for crate::adapter::A {}\n"),
           ("crates/linux/src/input/fake_adapter.rs", "impl SystemOps for crate::adapter::A {}\n"),
           ("crates/linux/src/input/mod.rs", "mod adapter;\n#[path = \"real_tests.rs\"]\nmod real_tests;\n"),
           ("crates/linux/src/input/keys.rs", "impl/* c /* n */ */SystemOps for crate::adapter::A {}\n")],
    "G9": [("crates/core/src/tree/leak.rs", "use crate::commands;\nfn f() { commands::version::execute(); }\n"),
           ("crates/core/src/tree/leak.rs", "use crate::{commands::version, tree};\n"),
           ("crates/core/src/lib.rs", CLEAN["crates/core/src/lib.rs"] + "pub(crate) use crate::commands::version::execute as command_version;\n")],
    "G10": [("crates/core/src/tree/orphan_tests.rs", "#[test]\nfn t() {}\n"),
            ("src/main.rs", CLEAN["src/main.rs"].replace("#[cfg(test)]\n#[path = \"tests/main_tests.rs\"]\nmod main_tests;\n", "")),
            ("crates/core/src/tree/node.rs", "pub struct Node;\nconst _: &str = r#\"#[path = \"node_tests.rs\"] mod tests;\"#;\n")],
    "G11": [("crates/core/src/tree/mod.rs", "pub mod node;\n#[cfg(test)]\n#[path = \"../input/mod.rs\"]\nmod misplaced;\n"),
            ("crates/core/src/tree/mod.rs", "pub mod node;\n#[cfg_attr(test, path = \"../input/mod.rs\")]\nmod misplaced;\n"),
            ("crates/core/src/tree/mod.rs", "pub mod node;\n#[path = \"@ABS@/crates/core/src/input/mod.rs\"]\nmod misplaced;\n")],
}
PLANTS["G12"] = [
    ("crates/macos/src/system/host.rs", "fn f() { let _ = std::os::unix::net::UnixListener::bind(\"/tmp/h.sock\"); }\n"),
    ("crates/core/src/system/host.rs", "fn f() { let _ = std::net::TcpListener::bind((\"127.0.0.1\", 0)); }\n"),
    ("crates/windows/src/system/host.rs", "fn f() { let _ = CreateNamedPipeW; }\n"),
    ("src/host.rs", "const MARKER: &str = \"AGENT_DESKTOP_INTERNAL_COMMAND_HOST=1\";\n"),
]
SUPPORT = {"G2": {"crates/core/custom.rs": "fn main() {}\n"},
           "G8": {"crates/linux/src/input/real_tests.rs": "impl SystemOps for crate::adapter::A {}\n"}}

# The C# clean fixture: the same shape the csharp/ tree must hold. Every domain folder
# carries one file, every platform project carries Adapter.cs plus the five layers, and
# each capability is implemented in its home.
CLEAN_CS = {
    "csharp/src/AgentDesktop.Core/AgentDesktop.Core.csproj":
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework>'
        '<RootNamespace>AgentDesktop.Core</RootNamespace><AssemblyName>AgentDesktop.Core</AssemblyName>'
        '</PropertyGroup><ItemGroup>'
        '<ProjectReference Include="../../tools/AgentDesktop.SourceGen/AgentDesktop.SourceGen.csproj" '
        'OutputItemType="Analyzer" ReferenceOutputAssembly="false" /></ItemGroup></Project>\n',
    "csharp/tools/AgentDesktop.SourceGen/AgentDesktop.SourceGen.csproj":
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>netstandard2.0</TargetFramework>'
        '<RootNamespace>AgentDesktop.SourceGen</RootNamespace><AssemblyName>AgentDesktop.SourceGen</AssemblyName>'
        '</PropertyGroup></Project>\n',
    "csharp/tools/AgentDesktop.SourceGen/Writer.cs":
        "namespace AgentDesktop.SourceGen;\npublic sealed class Writer;\n",
}
for _d in sorted(CS_CORE_DOMAINS):
    CLEAN_CS[f"csharp/src/AgentDesktop.Core/{_d}/{_d}.cs"] = (
        f"namespace AgentDesktop.Core.{_d};\npublic sealed class {_d};\n")
for _p in CS_PROJECT_SUFFIX:
    CLEAN_CS[f"csharp/src/AgentDesktop.{_p}/AgentDesktop.{_p}.csproj"] = (
        f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework>'
        f'<RootNamespace>AgentDesktop.{_p}</RootNamespace><AssemblyName>AgentDesktop.{_p}</AssemblyName>'
        f'</PropertyGroup><ItemGroup>'
        f'<ProjectReference Include="../AgentDesktop.Core/AgentDesktop.Core.csproj" /></ItemGroup></Project>\n')
    CLEAN_CS[f"csharp/src/AgentDesktop.{_p}/Adapter.cs"] = (
        f"namespace AgentDesktop.{_p};\npublic sealed partial class Adapter : "
        f"AgentDesktop.Core.Adapter.PlatformAdapter {{}}\n")
    for _layer, _trait in (("Tree", "ObservationOps"), ("Actions", "ActionOps"),
                           ("Input", "InputOps"), ("System", "SystemOps")):
        CLEAN_CS[f"csharp/src/AgentDesktop.{_p}/{_layer}/Adapter.cs"] = (
            f"namespace AgentDesktop.{_p};\npublic sealed partial class Adapter : "
            f"AgentDesktop.Core.Adapter.{_trait} {{}}\n")
    CLEAN_CS[f"csharp/src/AgentDesktop.{_p}/Notifications/NotificationInfo.cs"] = (
        f"namespace AgentDesktop.{_p}.Notifications;\npublic sealed class NotificationInfo;\n")
CLEAN_CS["csharp/src/AgentDesktop.Windows/Uia/Client.cs"] = (
    "namespace AgentDesktop.Windows.Uia;\npublic sealed class Client;\n")

CS_CORE = "csharp/src/AgentDesktop.Core/AgentDesktop.Core.csproj"
CS_WIN = "csharp/src/AgentDesktop.Windows/AgentDesktop.Windows.csproj"

CS_PLANTS = {
    "G1": [(CS_WIN, CLEAN_CS[CS_WIN].replace('</ItemGroup>',
              '<ProjectReference Include="../AgentDesktop.Linux/AgentDesktop.Linux.csproj" /></ItemGroup>')),
           (CS_CORE, CLEAN_CS[CS_CORE].replace('</ItemGroup>',
              '<ProjectReference Include="../AgentDesktop.Windows/AgentDesktop.Windows.csproj" /></ItemGroup>')),
           ("csharp/src/AgentDesktop.Extra/AgentDesktop.Extra.csproj",
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework>'
            '<AssemblyName>AgentDesktop.Extra</AssemblyName></PropertyGroup><ItemGroup>'
            '<ProjectReference Include="../AgentDesktop.Windows/AgentDesktop.Windows.csproj" /></ItemGroup></Project>'),
           ("csharp/tests/AgentDesktop.Windows.Tests/AgentDesktop.Windows.Tests.csproj",
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework>'
            '<AssemblyName>AgentDesktop.Windows.Tests</AssemblyName></PropertyGroup><ItemGroup>'
            '<ProjectReference Include="../../src/AgentDesktop.Linux/AgentDesktop.Linux.csproj" /></ItemGroup></Project>')],
    "G2": [(CS_CORE, CLEAN_CS[CS_CORE].replace('</PropertyGroup>',
              '<ItemGroup><PackageReference Include="System.Text.Json" Version="8.0.0" /></ItemGroup></PropertyGroup>')),
           (CS_CORE, CLEAN_CS[CS_CORE].replace('net8.0', 'net8.0-windows')),
           (CS_CORE, CLEAN_CS[CS_CORE].replace('<AssemblyName>', '<UseWindowsForms>true</UseWindowsForms><AssemblyName>')),
           ("csharp/src/AgentDesktop.Core/Directory.Build.props", "<Project></Project>\n")],
    "G3": [("csharp/src/AgentDesktop.Core/Tree/Leak.cs", "namespace AgentDesktop.Core.Tree;\nusing AgentDesktop.Windows;\npublic sealed class Leak;\n"),
           ("csharp/src/AgentDesktop.Windows/Tree/Leak.cs", "namespace AgentDesktop.Windows.Tree;\nusing AgentDesktop.Linux;\npublic sealed class Leak;\n")],
    "G4": [("csharp/src/AgentDesktop.Core/Tree/Leak.cs",
            "namespace AgentDesktop.Core.Tree;\npublic sealed class Leak { const string S = \"../../AgentDesktop.Windows/Uia/Client.cs\"; }\n"),
           ("csharp/src/AgentDesktop.Core/Tree/Leak.cs",
            "namespace AgentDesktop.Core.Tree;\npublic sealed class Leak { const string S = \"../../\\x41gentDesktop.Windows/Uia/Client.cs\"; }\n")],
    "G5": [("csharp/src/AgentDesktop.Core/Tree/Leak.cs",
            "namespace AgentDesktop.Core.Tree;\nusing System.Runtime.InteropServices;\npublic sealed class Leak { [DllImport(\"user32.dll\")] static extern int GetTickCount(); }\n"),
           ("csharp/src/AgentDesktop.Core/Tree/Leak.cs",
            "namespace AgentDesktop.Core.Tree;\npublic sealed class Leak { [SupportedOSPlatform(\"windows\")] static void F() {} }\n"),
           ("csharp/src/AgentDesktop.Core/Tree/Leak.cs",
            "namespace AgentDesktop.Core.Tree;\npublic sealed class Leak { static bool P => OperatingSystem.IsWindows(); }\n")],
    "G6": [("csharp/src/AgentDesktop.Windows/Win32.cs", "namespace AgentDesktop.Windows;\npublic sealed class Win32;\n")],
    "G7": [("csharp/src/AgentDesktop.Core/Loose.cs", "namespace AgentDesktop.Core;\npublic sealed class Loose;\n")],
    "G8": [("csharp/src/AgentDesktop.Windows/Input/Keyboard.cs",
            "namespace AgentDesktop.Windows;\npublic sealed partial class Adapter : AgentDesktop.Core.Adapter.SystemOps {}\n"),
           ("csharp/src/AgentDesktop.Windows/Input/Keyboard.cs",
            "using AgentDesktop.Core.Adapter.SystemOps as LifecycleOps;\nnamespace AgentDesktop.Windows;\npublic sealed partial class Adapter : LifecycleOps {}\n")],
    "G9": [("csharp/src/AgentDesktop.Core/Tree/Leak.cs",
            "namespace AgentDesktop.Core.Tree;\nusing AgentDesktop.Core.Commands;\npublic sealed class Leak;\n")],
    "G10": [("csharp/src/AgentDesktop.Core/Tree/NodeTests.cs",
             "namespace AgentDesktop.Core.Tree;\npublic sealed partial class Missing {}\n"),
            ("csharp/src/AgentDesktop.Core/Tree/NodeTests.cs",
             "namespace AgentDesktop.Core.Tree;\npublic sealed class NodeTests {}\n")],
}
CS_PLANTS["G12"] = [
    ("csharp/src/AgentDesktop.Windows/System/Host.cs",
     "namespace AgentDesktop.Windows.System;\nusing System.Net;\npublic sealed class Host { static void F() { var _ = new TcpListener(IPAddress.Any, 0); } }\n"),
    ("csharp/src/AgentDesktop.Windows/System/Host.cs",
     "namespace AgentDesktop.Windows.System;\npublic sealed class Host { const string M = \"AGENT_DESKTOP_INTERNAL_COMMAND_HOST=1\"; }\n"),
]


def self_test():
    """Plant one violation per case in a minimal clean workspace; every case must fire, the clean tree must pass."""
    global CORE_OS_ALLOWLIST, LISTENER_ALLOWLIST, CS_OS_ALLOWLIST, CS_LISTENER_ALLOWLIST
    saved = CORE_OS_ALLOWLIST
    saved_listeners = LISTENER_ALLOWLIST
    saved_cs = CS_OS_ALLOWLIST
    saved_cs_listeners = CS_LISTENER_ALLOWLIST
    tmp = tempfile.mkdtemp(prefix="arch-selftest-")
    try:
        def write(path, text):
            full = os.path.join(tmp, path)
            os.makedirs(os.path.dirname(full), exist_ok=True)
            with open(full, "w", encoding="utf-8") as fh:
                fh.write(text.replace("@ABS@", tmp.replace("\\", "/")))

        def build(overrides):
            for path, text in overrides.items():
                full = os.path.join(tmp, path)
                if text is None:
                    if os.path.exists(full):
                        os.remove(full)
                else:
                    write(path, text)

        def restore(path):
            original = CLEAN.get(path, CLEAN_CS.get(path))
            full = os.path.join(tmp, path)
            if original is None:
                if os.path.exists(full):
                    os.remove(full)
            else:
                write(path, original)

        CORE_OS_ALLOWLIST = set()
        LISTENER_ALLOWLIST = set()
        CS_OS_ALLOWLIST = set()
        CS_LISTENER_ALLOWLIST = set()
        build(dict(CLEAN, **CLEAN_CS))
        clean = check(tmp)
        if clean:
            raise SystemExit("self-test: the clean fixture workspace reports violations:\n  " + "\n  ".join(clean))
        for rule, cases in PLANTS.items():
            for path, text in cases:
                overrides = dict(SUPPORT.get(rule, {}))
                overrides[path] = text
                build(overrides)
                if not [e for e in check(tmp) if e.startswith(rule + ":")]:
                    raise SystemExit(f"self-test: {rule} did not fire for its planted case in {path}")
                for changed in overrides:
                    restore(changed)
        for rule, cases in CS_PLANTS.items():
            for path, text in cases:
                build({path: text})
                if not [e for e in check(tmp) if e.startswith(rule + ":")]:
                    raise SystemExit(f"self-test: {rule} did not fire for its planted C# case in {path}")
                restore(path)
        CORE_OS_ALLOWLIST = {"tree/node.rs"}
        if not [e for e in check(tmp) if e.startswith("G5:") and "no longer" in e]:
            raise SystemExit("self-test: a stale CORE_OS_ALLOWLIST entry was not reported")
        CORE_OS_ALLOWLIST = set()
        LISTENER_ALLOWLIST = {"crates/core/src/lib.rs"}
        if not [e for e in check(tmp) if e.startswith("G12:") and "no longer" in e]:
            raise SystemExit("self-test: a stale LISTENER_ALLOWLIST entry was not reported")
        LISTENER_ALLOWLIST = set()
        CS_OS_ALLOWLIST = {"Tree/Node.cs"}
        if not [e for e in check(tmp) if e.startswith("G5:") and "CS_OS_ALLOWLIST" in e]:
            raise SystemExit("self-test: a stale CS_OS_ALLOWLIST entry was not reported")
        CS_OS_ALLOWLIST = set()
        CS_LISTENER_ALLOWLIST = {"csharp/src/AgentDesktop.Windows/System/Host.cs"}
        build({"csharp/src/AgentDesktop.Windows/System/Host.cs":
               "namespace AgentDesktop.Windows.System;\nusing System.Net;\npublic sealed class Host { static void F() { var _ = new TcpListener(IPAddress.Any, 0); } }\n"})
        if [e for e in check(tmp) if e.startswith("G12:") and "CS_LISTENER_ALLOWLIST" in e]:
            raise SystemExit("self-test: an allowlisted C# listener was reported")
        restore("csharp/src/AgentDesktop.Windows/System/Host.cs")
        CS_LISTENER_ALLOWLIST = {"csharp/src/AgentDesktop.Core/Tree/Node.cs"}
        if not [e for e in check(tmp) if e.startswith("G12:") and "CS_LISTENER_ALLOWLIST" in e]:
            raise SystemExit("self-test: a stale CS_LISTENER_ALLOWLIST entry was not reported")
    finally:
        CORE_OS_ALLOWLIST = saved
        LISTENER_ALLOWLIST = saved_listeners
        CS_OS_ALLOWLIST = saved_cs
        CS_LISTENER_ALLOWLIST = saved_cs_listeners
        shutil.rmtree(tmp, ignore_errors=True)


def main():
    repo = os.path.abspath(sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith("--") else ".")
    if "--skip-self-test" not in sys.argv:
        self_test()
    errors = check(repo)
    for e in errors:
        print(e)
    if errors:
        print(f"architecture check failed: {len(errors)} violation(s)", file=sys.stderr)
        sys.exit(1)
    print("architecture check passed")


if __name__ == "__main__":
    main()
