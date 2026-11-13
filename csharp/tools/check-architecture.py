"""Architecture boundaries for the C# port of agent-desktop.

Fails when a platform project is referenced by core, when one platform project
reaches another, when a project's top-level layout drifts, when a capability is
implemented in the wrong layer, or when core carries an OS-specific branch
outside its allow-list. Project references are read from the .csproj files, so
a rename or a quoted key is seen exactly as MSBuild sees it.

Usage: check-architecture.py <root> [--skip-self-test]
"""
import os
import re
import sys
import tempfile
import xml.etree.ElementTree as ET

PLATFORMS = ("Macos", "Windows", "Linux")
PLATFORM_PROJECTS = {f"AgentDesktop.{p}" for p in PLATFORMS}
WIRING_PROJECTS = {"AgentDesktop.Cli", "AgentDesktop.Ffi"}
CORE_PROJECT = "AgentDesktop.Core"
CORE_DOMAINS = {
    "Actionability", "Actions", "Adapter", "Commands", "Context", "CursorOverlay", "Input",
    "Locator", "Notifications", "RefAction", "Refs", "Response", "Session", "Snapshot",
    "Storage", "Systems", "Trace", "Tree", "Wait",
}
PLATFORM_LAYERS = {"Tree", "Actions", "Input", "System", "Notifications"}
CORE_TOP_FILES = {"Lib.cs"}
BINARY_DIRS = {"Batch", "Cli", "CliArgs", "CommandPolicy", "Diagnostic", "Dispatch", "Mcp", "Tests", "VisualDebug"}
CAPABILITY_HOMES = {
    "PlatformAdapter": ("Adapter.cs",),
    "ObservationOps": ("Adapter.cs", "Tree/Adapter.cs"),
    "ActionOps": ("Adapter.cs", "Actions/Adapter.cs"),
    "InputOps": ("Adapter.cs", "Input/Adapter.cs"),
    "SystemOps": ("Adapter.cs", "System/Adapter.cs"),
}
CORE_OS_ALLOWLIST = set()
OS_TOKENS = ("System.Runtime.InteropServices", "RuntimeInformation", "OSPlatform", "OperatingSystem.", "DllImport(\"user32", "DllImport(\"libc", "NATIVE")


def read_references(csproj):
    tree = ET.parse(csproj)
    names = []
    for node in tree.iter():
        if node.tag.endswith("ProjectReference"):
            value = (node.get("Include") or node.text or "").strip()
            names.append(os.path.splitext(os.path.basename(value))[0])
    return names


def project_name(csproj):
    return os.path.splitext(os.path.basename(csproj))[0]


def check_references(projects):
    failures = []
    for name, references in projects.items():
        if name == CORE_PROJECT:
            for target in references:
                if target in PLATFORM_PROJECTS:
                    failures.append(f"{name} references {target}: core must not reach a platform adapter")
        if name in PLATFORM_PROJECTS:
            for target in references:
                if target in PLATFORM_PROJECTS:
                    failures.append(f"{name} references {target}: platform projects must not reach each other")
    return failures


def check_layout(root, projects):
    failures = []
    for name in projects:
        directory = find_project_dir(root, name)
        if directory is None:
            continue
        entries = [e for e in os.listdir(directory) if e not in (name + ".csproj",)]
        files = [e for e in entries if os.path.isfile(directory + os.sep + e) and e.endswith(".cs")]
        dirs = [e for e in entries if os.path.isdir(directory + os.sep + e) and e not in ("obj", "bin")]
        if name == CORE_PROJECT:
            for file in files:
                if file not in CORE_TOP_FILES:
                    failures.append(f"{name}: {file} sits at the project root, not in a domain folder")
            for folder in dirs:
                if folder not in CORE_DOMAINS:
                    failures.append(f"{name}: {folder} is not a core domain")
        if name in WIRING_PROJECTS:
            for folder in dirs:
                if folder not in BINARY_DIRS:
                    failures.append(f"{name}: {folder} is not a binary folder")
    return failures


def find_project_dir(root, name):
    for directory, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in ("obj", "bin")]
        if name + ".csproj" in files:
            return directory
    return None


def check_capability_homes(root, projects):
    failures = []
    for name in projects:
        directory = find_project_dir(root, name)
        if directory is None:
            continue
        for directory_root, dirs, files in os.walk(directory):
            dirs[:] = [d for d in dirs if d not in ("obj", "bin")]
            for file in files:
                if not file.endswith(".cs"):
                    continue
                path = os.path.join(directory_root, file)
                with open(path, encoding="utf-8") as handle:
                    text = handle.read()
                for capability, homes in CAPABILITY_HOMES.items():
                    if name in PLATFORM_PROJECTS and re.search(rf"public\s+.*\s{capability}\b", text):
                        relative = os.path.relpath(path, directory).replace("\\", "/")
                        if relative not in homes:
                            failures.append(f"{name}: {capability} is implemented in {relative}, not in {'/'.join(homes)}")
    return failures


def check_core_os_branches(root, projects):
    failures = []
    directory = find_project_dir(root, CORE_PROJECT)
    if directory is None:
        return failures
    for directory_root, dirs, files in os.walk(directory):
        dirs[:] = [d for d in dirs if d not in ("obj", "bin")]
        for file in files:
            if not file.endswith(".cs"):
                continue
            path = os.path.join(directory_root, file)
            relative = os.path.relpath(path, directory).replace("\\", "/")
            if relative in CORE_OS_ALLOWLIST:
                continue
            with open(path, encoding="utf-8") as handle:
                text = handle.read()
            for token in OS_TOKENS:
                if token in text:
                    failures.append(f"{CORE_PROJECT}: {relative} carries OS-conditional code ({token})")
    return failures


def self_test():
    """Plant one violation of every rule and confirm each one fires."""
    with tempfile.TemporaryDirectory() as root:
        core = os.path.join(root, "src", CORE_PROJECT)
        windows = os.path.join(root, "src", "AgentDesktop.Windows")
        os.makedirs(core)
        os.makedirs(windows)
        with open(os.path.join(core, CORE_PROJECT + ".csproj"), "w", encoding="utf-8") as handle:
            handle.write(f'<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../AgentDesktop.Windows/AgentDesktop.Windows.csproj" /></ItemGroup></Project>')
        with open(os.path.join(core, "Bad.cs"), "w", encoding="utf-8") as handle:
            handle.write("class Bad { }\n")
        with open(os.path.join(core, "Runtime.cs"), "w", encoding="utf-8") as handle:
            handle.write("using System.Runtime.InteropServices;\nclass R { }\n")
        with open(os.path.join(windows, "AgentDesktop.Windows.csproj"), "w", encoding="utf-8") as handle:
            handle.write("<Project Sdk=\"Microsoft.NET.Sdk\" />")
        with open(os.path.join(windows, "TreeAdapter.cs"), "w", encoding="utf-8") as handle:
            handle.write("public class X : ObservationOps { }\n")
        projects = {CORE_PROJECT: read_references(os.path.join(core, CORE_PROJECT + ".csproj")),
                    "AgentDesktop.Windows": read_references(os.path.join(windows, "AgentDesktop.Windows.csproj"))}
        checks = [
            check_references(projects),
            check_layout(root, projects),
            check_capability_homes(root, projects),
            check_core_os_branches(root, projects),
        ]
        for index, found in enumerate(checks):
            if not found:
                print(f"self-test failed: rule {index} planted a violation the gate did not detect")
                return False
    return True


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    if "--skip-self-test" not in sys.argv and not self_test():
        return 1
    projects = {}
    for directory, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in ("obj", "bin")]
        for file in files:
            if file.endswith(".csproj"):
                name = project_name(os.path.join(directory, file))
                projects[name] = read_references(os.path.join(directory, file))
    failures = check_references(projects) + check_layout(root, projects) + check_capability_homes(root, projects) + check_core_os_branches(root, projects)
    for failure in failures:
        print(failure)
    if failures:
        print(f"{len(failures)} architecture violations")
        return 1
    print("architecture is clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())
